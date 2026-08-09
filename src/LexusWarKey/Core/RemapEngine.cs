namespace LexusWarKey.Core;

public enum RemapAction
{
    /// <summary>Let the key through untouched.</summary>
    PassThrough,
    /// <summary>Swallow the original key and send <see cref="RemapDecision.SendVk"/> instead.</summary>
    SendKey,
    /// <summary>Swallow the original key and type the chat lines.</summary>
    SendChat,
    /// <summary>Swallow the key and click a command-card slot instead (position-based skills).</summary>
    ClickSlot,
}

public sealed record RemapDecision(
    RemapAction Action,
    int SendVk = 0,
    IReadOnlyList<string>? ChatLines = null,
    bool AlliesOnly = false,
    ScreenPoint? ClickAt = null,
    bool RightClick = false)
{
    public static readonly RemapDecision PassThrough = new(RemapAction.PassThrough);
}

/// <summary>Pure decision logic: given a key press and the current state, what should happen?
/// No Windows APIs here so every rule is unit-testable.
///
/// Design rules that keep this a remapper rather than an automation tool:
///  - exactly one key in, one key out (or one chat macro on one explicit press)
///  - modifiers are preserved, never synthesised
///  - key-up events are never rewritten into extra actions
///  - nothing fires unless the user physically presses a key</summary>
public sealed class RemapEngine
{
    private readonly Func<WarKeyProfile> _profile;
    private readonly Func<bool> _gameFocused;
    private readonly Func<bool>? _activated;
    private readonly Func<long> _nowMs;

    /// <param name="nowMs">Monotonic milliseconds. Injected so <see cref="ChatOpenFor"/> — the
    /// guard against a tracker stuck open — can be tested without sleeping for twenty seconds.</param>
    public RemapEngine(Func<WarKeyProfile> profile, Func<bool> gameFocused, Func<bool>? activated = null,
                       Func<long>? nowMs = null)
    {
        _profile = profile;
        _gameFocused = gameFocused;
        _activated = activated;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    /// <summary>Set while the app itself is typing a macro, so its own keystrokes are not remapped.</summary>
    public bool SuspendedForTyping { get; set; }

    /// <summary>True while Warcraft's own chat line is open.
    ///
    /// The game offers no way to ask, so it is inferred from the keys the player physically
    /// presses. It has to be tracked: with chat open a remapped Space is swallowed and re-sent
    /// as NumPad7, so "gg wp" is typed as "gg7wp", and a position-bound letter casts the ability
    /// instead of appearing in the message.
    ///
    /// Being wrong in the "open" direction silently disables everything, so the tracker only ever
    /// latches on keys the game actually received, and clears itself the moment the game is not
    /// focused. <see cref="ChatOpenChanged"/> lets the UI say so out loud rather than looking dead.</summary>
    public bool ChatOpen { get; private set; }

    /// <summary>How long the tracker has believed the chat line was open, measured from the
    /// keystroke that opened it. Zero when it is closed.
    ///
    /// This is the only handle on the tracker's one dangerous failure: if the game closes its
    /// chat line without an Enter or Escape reaching us — alt-tab mid-message is the known way —
    /// our idea of it inverts, every key passes through, and the app does nothing for the rest of
    /// the match without a single sign that anything is wrong. Real messages are sent in seconds;
    /// a line still "open" long after that is our mistake, not the player's typing.</summary>
    public TimeSpan ChatOpenFor =>
        ChatOpen ? TimeSpan.FromMilliseconds(_nowMs() - _chatOpenedMs) : TimeSpan.Zero;

    private long _chatOpenedMs;

    public event Action<bool>? ChatOpenChanged;

    /// <summary>Physical keys currently held, so OS auto-repeat cannot toggle the tracker.
    /// Holding Enter a beat too long used to flip it once per repeat and land on any parity.</summary>
    private readonly HashSet<int> _observedHeld = new();

    /// <summary>Feeds one physical keystroke to the chat-line tracker. Call before <see cref="Decide"/>:
    /// Enter opens the line, the next Enter sends and closes it, Escape closes it without sending.
    /// Injected keys must never reach this — the caller filters them out already.</summary>
    public void ObserveKey(int vk, bool isKeyDown)
    {
        if (!isKeyDown)
        {
            _observedHeld.Remove(vk);
            return;
        }

        // Outside the game there is no chat line to be open, and this doubles as the resync
        // for alt-tab, clicking away, and anything else that closed it behind our back.
        if (!_gameFocused())
        {
            SetChatOpen(false);
            return;
        }

        if (!_observedHeld.Add(vk))
            return; // auto-repeat of a held key, not a new press

        if (SuspendedForTyping)
            return;

        if (vk == VirtualKeys.Enter)
            SetChatOpen(!ChatOpen);
        else if (vk == VirtualKeys.Escape)
            SetChatOpen(false);
    }

    /// <summary>Does the profile bind this mouse control to anything? The mouse hook asks
    /// before swallowing an event, so an unbound wheel keeps zooming the camera.
    ///
    /// It deliberately ignores whether the app is enabled or the game is focused: those decide
    /// what an event DOES, and are re-checked in <see cref="Decide"/>. Here they would only
    /// make the hook let a claimed control through to the game as a raw wheel spin.</summary>
    public bool ClaimsMouseControl(int vk)
    {
        var profile = _profile();
        return profile.Inventory.Concat(profile.Skills).Any(m => m.ClaimsKey && m.FromVk == vk)
               || profile.ChatMacros.Any(m => m.IsUsable && m.HotkeyVk == vk);
    }

    /// <summary>True when anything at all is bound to the mouse, so the caller knows whether
    /// the mouse hook is worth installing.</summary>
    public bool AnyMouseControlBound()
    {
        var profile = _profile();
        return profile.Inventory.Concat(profile.Skills).Any(m => m.ClaimsKey && VirtualKeys.IsMouse(m.FromVk))
               || profile.ChatMacros.Any(m => m.IsUsable && VirtualKeys.IsMouse(m.HotkeyVk));
    }

    /// <summary>Forces the tracker shut. The app calls this whenever it cannot be sure any more —
    /// erring towards "closed" costs one garbled message, erring towards "open" costs every key.</summary>
    public void ResetChatState() => SetChatOpen(false);

    private void SetChatOpen(bool value)
    {
        if (ChatOpen == value)
            return;
        ChatOpen = value;
        if (value)
            _chatOpenedMs = _nowMs();
        ChatOpenChanged?.Invoke(value);
    }

    public RemapDecision Decide(int vk, bool isKeyDown, bool ctrlHeld, bool altHeld)
    {
        // Community gate: without a valid TierBot code every key passes through untouched.
        // The UI explains loudly; this just makes sure nothing works quietly around it.
        if (_activated is not null && !_activated())
            return RemapDecision.PassThrough;

        var profile = _profile();
        if (!profile.Enabled || SuspendedForTyping)
            return RemapDecision.PassThrough;
        if (profile.OnlyWhenGameFocused && !_gameFocused())
            return RemapDecision.PassThrough;

        // Enter and Escape drive the chat line itself. Remapping them would break the tracker
        // above and leave the player unable to close a chat box they cannot type into.
        if (vk is VirtualKeys.Enter or VirtualKeys.Escape)
            return RemapDecision.PassThrough;

        // Chat is open: the player is writing to other humans, so every key must arrive
        // as itself. This is the whole reason the tracker exists.
        if (ChatOpen)
            return RemapDecision.PassThrough;

        // Chat macros fire once, on key-down only, and never with a modifier held
        // (Ctrl+F6 must stay available for the app's own overlay shortcut).
        if (isKeyDown && !ctrlHeld && !altHeld)
        {
            var macro = profile.ChatMacros.FirstOrDefault(m => m.IsUsable && m.HotkeyVk == vk);
            if (macro is not null)
            {
                var lines = macro.Messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
                return new RemapDecision(RemapAction.SendChat, ChatLines: lines, AlliesOnly: macro.AlliesOnly);
            }
        }

        // Skills: the bound key acts on the ability in that command-card CELL, whatever it is —
        // the only stable handle in random-ability modes (LoD). Two mechanisms, per cell:
        //
        //  - CustomKeys mode (UseGameLetters + a letter on the cell): the generated
        //    CustomKeys.txt already gave every LoD ability its cell's letter inside the game,
        //    so the cell's key is simply remapped onto that LETTER. Pure keystroke: no cursor,
        //    no click queue, and Alt+key (auto-cast) / Ctrl+key (learn) arrive as the game
        //    expects because modifiers pass through untouched.
        //
        //  - Click fallback (no letter on the cell): the cell is clicked as before. This stays
        //    for shadowed pairs — two picked skills sharing one letter, where the game answers
        //    the letter with only one of them and the other is reachable by position alone.
        if (profile.SkillsUsePosition)
        {
            var slot = profile.Skills.FindIndex(m => m.Enabled && m.FromVk == vk && m.FromVk != 0);
            if (slot >= 0)
            {
                var skill = profile.Skills[slot];
                if (profile.UseGameLetters && skill.ToVk != 0)
                {
                    // Key already IS the letter: the game handles it natively, and swallowing
                    // it to re-send the same key would only add a hook round-trip.
                    return skill.ToVk == vk
                        ? RemapDecision.PassThrough
                        : new RemapDecision(RemapAction.SendKey, skill.ToVk);
                }

                if (profile.CommandCard.IsCalibrated)
                {
                    if (!isKeyDown)
                        return new RemapDecision(RemapAction.ClickSlot); // swallow the key-up, click already happened
                    var point = profile.CommandCard.PointFor(slot);
                    return point is null
                        ? RemapDecision.PassThrough
                        : new RemapDecision(RemapAction.ClickSlot, ClickAt: point, RightClick: altHeld);
                }

                // No calibration: a cell that happens to carry a target key still works as a
                // plain remap — pre-position profiles have relied on this since v1.
                return skill.IsUsable
                    ? new RemapDecision(RemapAction.SendKey, skill.ToVk)
                    : RemapDecision.PassThrough;
            }
        }

        // Inventory remaps apply to both down and up so the game sees a complete keystroke.
        // Modifiers are deliberately NOT consumed: Alt+key (auto-cast) and Ctrl+key
        // (learn skill) keep working, just with the remapped base key.
        var map = profile.Inventory.FirstOrDefault(m => m.IsUsable && m.FromVk == vk);
        return map is null ? RemapDecision.PassThrough : new RemapDecision(RemapAction.SendKey, map.ToVk);
    }

    /// <summary>Keys the user has bound that currently do nothing, with the reason.
    /// Without this, a skill key bound before calibrating just silently fails.</summary>
    public static IReadOnlyList<string> FindDeadBindings(WarKeyProfile profile)
    {
        var problems = new List<string>();

        if (!profile.Enabled)
            problems.Add("Апп унтраалттай байна — ямар ч товч ажиллахгүй. Дээрх 'Идэвхтэй'-г тэмдэглэ.");

        // Cells running in CustomKeys letter mode never click, so they work uncalibrated; and a
        // cell that is a usable plain remap falls back to remapping on an unlinked card (the v1
        // promise). Only cells that would have to CLICK are dead without a linked card — the
        // old count included the remap fallback and warned about keys that actually worked.
        var clickingSkills = profile.Skills.Count(m =>
            m.ClaimsKey && !(profile.UseGameLetters && m.ToVk != 0) && !m.IsUsable);
        if (clickingSkills > 0 && !profile.CommandCard.IsCalibrated)
        {
            // Saying what is wrong without saying what to do is how this sat unresolved:
            // calibration needs the game on screen, which is not obvious from the button.
            problems.Add($"{clickingSkills} чадварын товч ажиллахгүй байна — командын карт холбоогүй. " +
                         "Тоглоом дотроо Ctrl+F6 дараад картаа тоглоомын карт дээр давхарлаж 'Холбох'-ыг дар.");
        }

        var inventoryWithoutTarget = profile.Inventory.Count(m => m.ClaimsKey && m.ToVk == 0);
        if (inventoryWithoutTarget > 0)
            problems.Add($"{inventoryWithoutTarget} эд зүйлийн товчид тоглоомын товч алга.");

        // Several macros on one key looks like "this key sends all of these", but only the first
        // is ever found. Naming the winner is the point: without it the rest fail invisibly, and
        // the user has no way to tell which of their lines is the one getting through.
        foreach (var group in profile.ChatMacros.Where(m => m.IsUsable).GroupBy(m => m.HotkeyVk).Where(g => g.Count() > 1))
        {
            var first = group.First().Messages.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "";
            problems.Add(
                $"{VirtualKeys.NameOf(group.Key)} дээр {group.Count()} макро байна — зөвхөн эхнийх нь (\"{first}\") илгээгдэнэ. " +
                "Бүгдийг нэг дор явуулах бол нэг макро дотор мөр бүрт нэг мессеж бичнэ үү.");
        }

        return problems;
    }

    /// <summary>Duplicate source keys would make behaviour undefined; the UI shows these.
    /// Uses ClaimsKey rather than IsUsable so position-based skills — which have no target
    /// key — are still checked against everything else.</summary>
    public static IReadOnlyList<int> FindConflicts(WarKeyProfile profile)
    {
        var sources = profile.Inventory.Concat(profile.Skills)
            .Where(m => m.ClaimsKey).Select(m => m.FromVk)
            .Concat(profile.ChatMacros.Where(m => m.IsUsable).Select(m => m.HotkeyVk));

        return sources.GroupBy(vk => vk).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    }
}
