namespace LexusWarKey.Core;

public enum RemapAction
{
    PassThrough,
    SendKey,
    SendChat,
}

public sealed record RemapDecision(
    RemapAction Action,
    int SendVk = 0,
    IReadOnlyList<string>? ChatLines = null)
{
    public static readonly RemapDecision PassThrough = new(RemapAction.PassThrough);
}

/// <summary>Pure decision logic: one physical key becomes one Warcraft skill letter.
/// QuickChat is the only non-remap action and it fires from two explicit user-configured slots.</summary>
public sealed class RemapEngine
{
    private readonly Func<WarKeyProfile> _profile;
    private readonly Func<bool> _gameFocused;
    private readonly Func<long> _nowMs;

    public RemapEngine(Func<WarKeyProfile> profile, Func<bool> gameFocused, Func<bool>? activated = null,
                       Func<long>? nowMs = null)
    {
        _ = activated;
        _profile = profile;
        _gameFocused = gameFocused;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    /// <summary>Set while the app itself is typing QuickChat, so its injected keys are not remapped.</summary>
    public bool SuspendedForTyping { get; set; }

    /// <summary>True while Warcraft's own chat line is open. The game exposes no state for this,
    /// so the app infers it from the player's physical Enter/Escape presses.</summary>
    public bool ChatOpen { get; private set; }

    public TimeSpan ChatOpenFor =>
        ChatOpen ? TimeSpan.FromMilliseconds(_nowMs() - _chatOpenedMs) : TimeSpan.Zero;

    private long _chatOpenedMs;
    private readonly HashSet<int> _observedHeld = new();

    public event Action<bool>? ChatOpenChanged;

    public void ObserveKey(int vk, bool isKeyDown)
    {
        if (!isKeyDown)
        {
            _observedHeld.Remove(vk);
            return;
        }

        if (!_gameFocused())
        {
            SetChatOpen(false);
            return;
        }

        if (!_observedHeld.Add(vk))
            return;
        if (SuspendedForTyping)
            return;

        if (vk == VirtualKeys.Enter)
            SetChatOpen(!ChatOpen);
        else if (vk == VirtualKeys.Escape)
            SetChatOpen(false);
    }

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
        var profile = _profile();
        if (!profile.Enabled || SuspendedForTyping)
            return RemapDecision.PassThrough;
        if (!_gameFocused())
            return RemapDecision.PassThrough;
        if (vk is VirtualKeys.Enter or VirtualKeys.Escape)
            return RemapDecision.PassThrough;
        if (ChatOpen)
            return RemapDecision.PassThrough;

        // QuickChat is exactly two single-message slots. Modifiers are ignored so Ctrl+F6
        // remains the app's own in-game editor shortcut and Warcraft modifier actions survive.
        if (isKeyDown && !ctrlHeld && !altHeld)
        {
            var macro = profile.ChatMacros.Take(WarKeyProfile.QuickChatSlots)
                .FirstOrDefault(m => m.IsUsable && m.HotkeyVk == vk);
            if (macro is not null)
                return new RemapDecision(RemapAction.SendChat, ChatLines: new[] { macro.Message });
        }

        var map = profile.Skills.FirstOrDefault(m => m.IsUsable && m.FromVk == vk);
        return map is null ? RemapDecision.PassThrough : new RemapDecision(RemapAction.SendKey, map.ToVk);
    }

    public static IReadOnlyList<string> FindDeadBindings(WarKeyProfile profile)
    {
        var problems = new List<string>();

        if (!profile.Enabled)
            problems.Add("App is disabled; no key remaps will run.");

        var letterless = profile.Skills.Count(m => m.ClaimsKey && m.ToVk == 0);
        if (letterless > 0)
            problems.Add($"{letterless} skill slot(s) have your key but no Warcraft letter.");

        return problems;
    }

    public static IReadOnlyList<int> FindConflicts(WarKeyProfile profile)
    {
        var sources = profile.Skills
            .Where(m => m.ClaimsKey).Select(m => m.FromVk)
            .Concat(profile.ChatMacros.Take(WarKeyProfile.QuickChatSlots)
                .Where(m => m.IsUsable).Select(m => m.HotkeyVk));

        return sources.GroupBy(vk => vk).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    }
}
