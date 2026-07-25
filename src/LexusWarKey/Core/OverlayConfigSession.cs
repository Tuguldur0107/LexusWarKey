namespace LexusWarKey.Core;

public enum OverlayStep
{
    /// <summary>Waiting for 1-6 to pick an inventory slot.</summary>
    ChoosingSlot,
    /// <summary>A slot is picked; the next key press becomes its binding.</summary>
    WaitingForKey,
}

public sealed record OverlayState(OverlayStep Step, int SelectedIndex, string Prompt);

/// <summary>The in-game rebinding flow, kept free of Windows APIs so it can be tested:
/// press 1-6 to pick a slot, press any key to bind it, Backspace clears, Esc closes.</summary>
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

    public string Prompt => Step == OverlayStep.ChoosingSlot
        ? "1–6 дарж нүд сонго"
        : $"Нүд {SelectedIndex + 1} — товчоо дар (Backspace = цэвэрлэх)";

    /// <summary>Handles one key press. Returns false when the overlay should close.</summary>
    public bool HandleKey(int vk)
    {
        if (vk == VirtualKeys.Escape)
            return false;

        if (Step == OverlayStep.ChoosingSlot)
        {
            var index = SlotIndexFor(vk);
            if (index >= 0 && index < _profile.Inventory.Count)
            {
                SelectedIndex = index;
                Step = OverlayStep.WaitingForKey;
            }
            return true;
        }

        var slot = _profile.Inventory[SelectedIndex];
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

        Step = OverlayStep.ChoosingSlot;
        SelectedIndex = -1;
        _onChanged();
        return true;
    }

    public void Reset()
    {
        Step = OverlayStep.ChoosingSlot;
        SelectedIndex = -1;
    }

    /// <summary>Accepts both the number row and the numpad for picking a slot.</summary>
    public static int SlotIndexFor(int vk) => vk switch
    {
        >= '1' and <= '6' => vk - '1',
        >= VirtualKeys.NumPad1 and <= VirtualKeys.NumPad6 => vk - VirtualKeys.NumPad1,
        _ => -1,
    };
}
