namespace LexusWarKey.Core;

/// <summary>One skill shown in the in-game Ctrl+F6 list: its id, name and default letter.</summary>
public sealed record OverlaySkill(string Id, string Name, char Default);

public enum OverlayStep
{
    ChoosingTarget,
    WaitingForLetter,   // a skill is selected: press the letter to give it
    WaitingForKey,      // an inventory slot is selected: press your key
}

/// <summary>The Ctrl+F6 in-game flow, over the LIVE detected skills AND the inventory slots. Because
/// the overlay never takes focus, Warcraft stays focused, so the background writer keeps running and
/// each change applies at once - the player configures without alt-tabbing.
///
/// The list is skills first (indices 0..N-1), then the six inventory slots (N..N+5). Press a number
/// to pick a row; for a skill, press the letter you want it on; for an inventory slot, press your own
/// key (its game key is the fixed numpad hotkey).</summary>
public sealed class OverlayConfigSession
{
    private readonly WarKeyProfile _profile;
    private readonly Func<IReadOnlyList<OverlaySkill>> _detected;
    private readonly Action _onChanged;

    public OverlayConfigSession(WarKeyProfile profile, Func<IReadOnlyList<OverlaySkill>> detected, Action onChanged)
    {
        _profile = profile;
        _detected = detected;
        _onChanged = onChanged;
    }

    public OverlayStep Step { get; private set; } = OverlayStep.ChoosingTarget;
    public int SelectedIndex { get; private set; } = -1;

    public IReadOnlyList<OverlaySkill> Skills => _detected();
    public int SkillCount => Skills.Count;
    public int InventoryCount => _profile.Inventory.Count;
    public int TotalCount => SkillCount + InventoryCount;

    public bool IsInventory(int index) => index >= SkillCount && index < TotalCount;
    private int InventorySlot(int index) => index - SkillCount;

    public string Prompt => Step switch
    {
        OverlayStep.ChoosingTarget => TotalCount == 0
            ? "No skills detected - select your hero"
            : $"Press a number 1-{TotalCount}, then a key",
        OverlayStep.WaitingForLetter => $"{TargetName}: press the letter (Backspace clears, Esc cancels)",
        _ => $"{TargetName}: press your key (Backspace clears, Esc cancels)",
    };

    private string TargetName
    {
        get
        {
            if (SelectedIndex < 0)
                return "?";
            if (IsInventory(SelectedIndex))
                return $"Item {InventorySlot(SelectedIndex) + 1}";
            return SelectedIndex < SkillCount ? Skills[SelectedIndex].Name : "?";
        }
    }

    public string AssignedOf(string id) => _profile.SkillLetters.GetValueOrDefault(id, "");

    public void SelectTarget(int index)
    {
        if (index < 0 || index >= TotalCount)
            return;
        SelectedIndex = index;
        Step = IsInventory(index) ? OverlayStep.WaitingForKey : OverlayStep.WaitingForLetter;
    }

    /// <summary>Handles one key. Returns false when the overlay should close.</summary>
    public bool HandleKey(int vk)
    {
        if (vk == VirtualKeys.Escape)
        {
            if (Step != OverlayStep.ChoosingTarget)
            {
                Reset();
                return true;
            }
            return false;
        }

        if (Step == OverlayStep.ChoosingTarget)
        {
            var index = SkillIndexFor(vk);
            if (index >= 0 && index < TotalCount)
                SelectTarget(index);
            return true;
        }

        if (Step == OverlayStep.WaitingForKey)   // inventory: capture the player's own key
        {
            if (SelectedIndex < 0 || !IsInventory(SelectedIndex))
            {
                Reset();
                return true;
            }
            var slotIndex = InventorySlot(SelectedIndex);
            if (vk == VirtualKeys.Back)
            {
                _profile.Inventory[slotIndex].FromVk = 0;
                _profile.Inventory[slotIndex].Enabled = false;
            }
            else if (vk != VirtualKeys.Enter)
            {
                _profile.SetInventoryKey(slotIndex, vk);   // unique across inventory + skills
            }
            else
            {
                return true;   // Enter drives chat; keep waiting
            }
            Reset();
            _onChanged();
            return true;
        }

        // WaitingForLetter: skill
        var skills = Skills;
        if (SelectedIndex < 0 || SelectedIndex >= skills.Count)
        {
            Reset();
            return true;
        }
        var id = skills[SelectedIndex].Id;
        if (vk == VirtualKeys.Back)
        {
            _profile.ClearSkillLetter(id);
            Reset();
            _onChanged();
            return true;
        }
        if (vk is >= 'A' and <= 'Z')
        {
            _profile.SetSkillLetter(id, ((char)vk).ToString());
            Reset();
            _onChanged();
            return true;
        }
        return true;   // not a letter; keep waiting
    }

    public void Reset()
    {
        Step = OverlayStep.ChoosingTarget;
        SelectedIndex = -1;
    }

    public static int SkillIndexFor(int vk) => vk switch
    {
        >= '1' and <= '9' => vk - '1',
        >= VirtualKeys.NumPad1 and <= VirtualKeys.NumPad9 => vk - VirtualKeys.NumPad1,
        _ => -1,
    };
}
