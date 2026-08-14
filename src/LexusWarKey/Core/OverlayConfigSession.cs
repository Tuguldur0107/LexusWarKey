namespace LexusWarKey.Core;

public enum OverlayStep
{
    ChoosingSlot,
    WaitingForKey,
    WaitingForLetter,
}

/// <summary>The Ctrl+F6 in-game flow. It edits only the skill grid:
/// choose a cell, press the key you want to use, press that ability's Warcraft letter.</summary>
public sealed class OverlayConfigSession
{
    private readonly WarKeyProfile _profile;
    private readonly Action _onChanged;

    public OverlayConfigSession(WarKeyProfile profile, Action onChanged)
    {
        _profile = profile;
        _onChanged = onChanged;
    }

    public OverlayStep Step { get; private set; } = OverlayStep.ChoosingSlot;
    public int SelectedIndex { get; private set; } = -1;

    public string Prompt => Step switch
    {
        OverlayStep.ChoosingSlot => "Select skill 1-8",
        OverlayStep.WaitingForKey => $"Skill {SelectedIndex + 1}: press your key (Backspace clears, Esc cancels)",
        _ => $"Skill {SelectedIndex + 1}: press Warcraft letter"
             + CurrentLetterSuffix(),
    };

    private string CurrentLetterSuffix()
    {
        var current = _profile.Skills[SelectedIndex].ToVk;
        return current == 0 ? "" : $" (current {VirtualKeys.NameOf(current)}, Enter keeps it)";
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= _profile.Skills.Count)
            return;
        SelectedIndex = index;
        Step = OverlayStep.WaitingForKey;
    }

    /// <summary>Handles one key press. Returns false when the overlay should close.</summary>
    public bool HandleKey(int vk)
    {
        if (vk == VirtualKeys.Escape)
        {
            if (Step != OverlayStep.ChoosingSlot)
            {
                Reset();
                return true;
            }
            return false;
        }

        if (Step == OverlayStep.ChoosingSlot)
        {
            var index = SkillIndexFor(vk);
            if (index >= 0)
                SelectSlot(index);
            return true;
        }

        var slot = _profile.Skills[SelectedIndex];

        if (Step == OverlayStep.WaitingForLetter)
        {
            if (vk == VirtualKeys.Back)
            {
                Clear(slot);
                Reset();
                _onChanged();
                return true;
            }

            if (vk == VirtualKeys.Enter)
            {
                if (slot.ToVk == 0)
                    return true;
            }
            else
            {
                slot.ToVk = vk;
            }

            slot.Enabled = slot.FromVk != 0;
            Reset();
            _onChanged();
            return true;
        }

        if (vk == VirtualKeys.Back)
        {
            Clear(slot);
            Reset();
            _onChanged();
            return true;
        }

        // Enter drives Warcraft's chat line, so RemapEngine.Decide passes it through before it
        // ever reaches the skill lookup. Accepting it here would store a cell that reads
        // "Enter->T", looks configured, is not reported as dead, and never casts anything.
        // Keep waiting for a key that can actually work. (Escape is handled at the top as
        // "cancel", so it can never get this far.)
        if (vk == VirtualKeys.Enter)
            return true;

        slot.FromVk = vk;
        slot.Enabled = true;
        Step = OverlayStep.WaitingForLetter;
        return true;
    }

    public void Reset()
    {
        Step = OverlayStep.ChoosingSlot;
        SelectedIndex = -1;
    }

    public static int SkillIndexFor(int vk) => vk switch
    {
        >= '1' and <= '8' => vk - '1',
        >= VirtualKeys.NumPad1 and <= VirtualKeys.NumPad8 => vk - VirtualKeys.NumPad1,
        _ => -1,
    };

    private static void Clear(KeyMap slot)
    {
        slot.FromVk = 0;
        slot.ToVk = 0;
        slot.Enabled = false;
    }
}
