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

    public RemapEngine(Func<WarKeyProfile> profile, Func<bool> gameFocused)
    {
        _profile = profile;
        _gameFocused = gameFocused;
    }

    /// <summary>Set while the user is typing in chat, so macros and remaps do not fire mid-message.</summary>
    public bool SuspendedForTyping { get; set; }

    public RemapDecision Decide(int vk, bool isKeyDown, bool ctrlHeld, bool altHeld)
    {
        var profile = _profile();
        if (!profile.Enabled || SuspendedForTyping)
            return RemapDecision.PassThrough;
        if (profile.OnlyWhenGameFocused && !_gameFocused())
            return RemapDecision.PassThrough;

        // Chat macros fire once, on key-down only, and never with a modifier held
        // (Ctrl+F5 etc. must stay available for the app's own shortcuts).
        if (isKeyDown && !ctrlHeld && !altHeld)
        {
            var macro = profile.ChatMacros.FirstOrDefault(m => m.IsUsable && m.HotkeyVk == vk);
            if (macro is not null)
            {
                var lines = macro.Messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
                return new RemapDecision(RemapAction.SendChat, ChatLines: lines, AlliesOnly: macro.AlliesOnly);
            }
        }

        // Skills by POSITION: the ability in that command-card slot is clicked, whatever
        // letter it happens to have. This is what makes random-ability modes (LoD) workable.
        // Alt is passed through as a right-click, which is how auto-cast is toggled.
        if (profile.SkillsUsePosition && profile.CommandCard.IsCalibrated)
        {
            var slot = profile.Skills.FindIndex(m => m.Enabled && m.FromVk == vk && m.FromVk != 0);
            if (slot >= 0)
            {
                if (!isKeyDown)
                    return new RemapDecision(RemapAction.ClickSlot); // swallow the key-up, click already happened
                var point = profile.CommandCard.PointFor(slot);
                return point is null
                    ? RemapDecision.PassThrough
                    : new RemapDecision(RemapAction.ClickSlot, ClickAt: point, RightClick: altHeld);
            }
        }

        // Key remaps apply to both down and up so the game sees a complete keystroke.
        // Modifiers are deliberately NOT consumed: Alt+key (auto-cast) and Ctrl+key
        // (learn skill) keep working, just with the remapped base key.
        var maps = profile.SkillsUsePosition && profile.CommandCard.IsCalibrated
            ? profile.Inventory
            : profile.Inventory.Concat(profile.Skills);
        var map = maps.FirstOrDefault(m => m.IsUsable && m.FromVk == vk);
        return map is null ? RemapDecision.PassThrough : new RemapDecision(RemapAction.SendKey, map.ToVk);
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
