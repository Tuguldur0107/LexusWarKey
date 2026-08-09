namespace LexusWarKey.Core;

public enum SlotGroup
{
    Inventory,
    Skill,
}

public enum OverlayStep
{
    /// <summary>Waiting for the user to pick a slot (click it, or press 1-6 for inventory).</summary>
    ChoosingSlot,
    /// <summary>A slot is picked; the next key press becomes its binding.</summary>
    WaitingForKey,
    /// <summary>CustomKeys mode, skill slots only: the next key press becomes the GAME letter
    /// this cell answers to (the letter the app will send instead of clicking).</summary>
    WaitingForLetter,
}

/// <summary>The in-game rebinding flow, kept free of Windows APIs so it can be tested.
///
/// Slots are picked by clicking them in the overlay — with 6 inventory plus 12 card slots
/// there are not enough digits to go round, and clicking works even though the overlay
/// never takes focus away from the game. Digits 1-6 stay as a shortcut for inventory.</summary>
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
    public SlotGroup SelectedGroup { get; private set; }
    public int SelectedIndex { get; private set; } = -1;

    public string Prompt => Step switch
    {
        OverlayStep.ChoosingSlot =>
            "Нүд дээр дар (эсвэл 1–6 = эд зүйл), дараа нь товчоо дар",
        OverlayStep.WaitingForKey =>
            $"{GroupName(SelectedGroup)} {SelectedIndex + 1} — товчоо дар (Backspace = цэвэрлэх, Esc = болих)",
        _ =>
            $"Чадвар {SelectedIndex + 1} — тоглоомын ҮСГИЙГ дар " +
            $"(одоо: {LetterName(ListFor(SelectedGroup)[SelectedIndex].ToVk)} · Enter = хэвээр · Backspace = клик горим)",
    };

    private static string LetterName(int vk) => vk == 0 ? "клик" : VirtualKeys.NameOf(vk);

    private static string GroupName(SlotGroup group) => group == SlotGroup.Inventory ? "Эд зүйл" : "Чадвар";

    private List<KeyMap> ListFor(SlotGroup group) =>
        group == SlotGroup.Inventory ? _profile.Inventory : _profile.Skills;

    /// <summary>Selects a slot directly — used when the user clicks one in the overlay.
    /// Works for empty slots too, which is the whole point of adding a binding.</summary>
    public void SelectSlot(SlotGroup group, int index)
    {
        if (index < 0 || index >= ListFor(group).Count)
            return;
        SelectedGroup = group;
        SelectedIndex = index;
        Step = OverlayStep.WaitingForKey;
    }

    /// <summary>Handles one key press. Returns false when the overlay should close.</summary>
    public bool HandleKey(int vk)
    {
        if (vk == VirtualKeys.Escape)
        {
            // Esc backs out of a slot first, and only closes when nothing is selected.
            if (Step != OverlayStep.ChoosingSlot)
            {
                Reset();
                return true;
            }
            return false;
        }

        if (Step == OverlayStep.ChoosingSlot)
        {
            var index = InventoryIndexFor(vk);
            if (index >= 0)
                SelectSlot(SlotGroup.Inventory, index);
            return true;
        }

        var slot = ListFor(SelectedGroup)[SelectedIndex];

        if (Step == OverlayStep.WaitingForLetter)
        {
            // A wheel notch or side button can arrive here mid-adjustment, but a mouse control
            // cannot be a game LETTER — SendKey of a mouse pseudo-vk types nothing, and the
            // cell would silently die. Stay in this step until a real key answers.
            if (VirtualKeys.IsMouse(vk))
                return true;

            // The letter is what the app SENDS for this cell; empty means click the cell.
            if (vk == VirtualKeys.Back)
                slot.ToVk = 0;
            else if (vk != VirtualKeys.Enter)
                slot.ToVk = vk;

            Reset();
            _onChanged();
            return true;
        }

        if (vk == VirtualKeys.Back)
        {
            slot.FromVk = 0;
            slot.Enabled = false;
        }
        else
        {
            slot.FromVk = vk;
            slot.Enabled = true;
        }

        // In CustomKeys mode a skill cell has a second half to its binding — the game letter
        // it answers to. Offer it right away, pre-seeded with the scheme default, so the whole
        // rebind stays one fluent mid-match gesture: click cell, press key, press letter.
        if (SelectedGroup == SlotGroup.Skill && slot.Enabled && _profile.UseGameLetters)
        {
            if (slot.ToVk == 0 && SelectedIndex < WarKeyProfile.DefaultSlotLetters.Length)
                slot.ToVk = WarKeyProfile.DefaultSlotLetters[SelectedIndex];
            Step = OverlayStep.WaitingForLetter;
            _onChanged();
            return true;
        }

        Reset();
        _onChanged();
        return true;
    }

    public void Reset()
    {
        Step = OverlayStep.ChoosingSlot;
        SelectedIndex = -1;
    }

    /// <summary>Digit shortcut for inventory slots; accepts the number row and the numpad.</summary>
    public static int InventoryIndexFor(int vk) => vk switch
    {
        >= '1' and <= '6' => vk - '1',
        >= VirtualKeys.NumPad1 and <= VirtualKeys.NumPad6 => vk - VirtualKeys.NumPad1,
        _ => -1,
    };
}
