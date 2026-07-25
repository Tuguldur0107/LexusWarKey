using System.Text.Json.Serialization;

namespace LexusWarKey.Core;

/// <summary>One key translation: press <see cref="FromVk"/>, the game receives <see cref="ToVk"/>.
/// Strictly one key in, one key out — this is a remapper, never an action automator.</summary>
public sealed class KeyMap
{
    public int FromVk { get; set; }
    public int ToVk { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Ready to remap one key onto another.</summary>
    [JsonIgnore] public bool IsUsable => Enabled && FromVk != 0 && ToVk != 0 && FromVk != ToVk;

    /// <summary>The user has claimed this key, whether or not it currently does anything.
    /// Position-based skills have no target key, so conflict checks must use this.</summary>
    [JsonIgnore] public bool ClaimsKey => Enabled && FromVk != 0;
}

/// <summary>A hotkey that types one or more chat lines, in order, on a single press.</summary>
public sealed class ChatMacro
{
    public int HotkeyVk { get; set; }
    public List<string> Messages { get; set; } = new();
    public bool Enabled { get; set; } = true;
    /// <summary>False = normal chat (Enter), true = allies only (Shift+Enter in Warcraft III).</summary>
    public bool AlliesOnly { get; set; }

    [JsonIgnore] public bool IsUsable => Enabled && HotkeyVk != 0 && Messages.Any(m => !string.IsNullOrWhiteSpace(m));
}

public sealed class WarKeyProfile
{
    /// <summary>Inventory slots 1..6 mapped onto Warcraft's NumPad defaults.</summary>
    public List<KeyMap> Inventory { get; set; } = new();

    /// <summary>Free-form remaps: hero abilities, Invoker combos, anything else.</summary>
    public List<KeyMap> Skills { get; set; } = new();

    public List<ChatMacro> ChatMacros { get; set; } = new();

    /// <summary>Where the 4x3 command card is on screen, so skills can be triggered by
    /// POSITION instead of letter (required for LoD-style modes with random abilities).</summary>
    public CommandCard CommandCard { get; set; } = new();

    /// <summary>True = a skill key clicks its command-card slot; false = plain key remapping.</summary>
    public bool SkillsUsePosition { get; set; } = true;

    /// <summary>Off by default: clicks are posted to the game window so the real cursor never
    /// moves. Turn on only if the game ignores posted messages — then the cursor is moved and
    /// put straight back, which is briefly visible.</summary>
    public bool MoveCursorForClicks { get; set; }

    /// <summary>Where the user dragged the in-game overlay to; null = default corner.</summary>
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }

    /// <summary>Remapping only acts while Warcraft III has focus — never in other programs.</summary>
    public bool OnlyWhenGameFocused { get; set; } = true;
    public bool Enabled { get; set; } = true;

    /// <summary>Warcraft's inventory is a 2x3 grid and the ability area of its command card is
    /// 4x2, so the UI mirrors those shapes exactly instead of showing a flat list.</summary>
    public const int InventorySlots = 6;
    public const int SkillSlots = CommandCard.Slots;

    public static WarKeyProfile CreateDefault()
    {
        var profile = new WarKeyProfile();

        // Slot 1 = Space and slot 2 = Tab match the setup in the user's Garena screenshot;
        // the rest start empty so nothing is remapped without being asked for.
        var starters = new[] { VirtualKeys.Space, VirtualKeys.Tab, 0, 0, 0, 0 };
        for (var i = 0; i < InventorySlots; i++)
        {
            profile.Inventory.Add(new KeyMap
            {
                FromVk = starters[i],
                ToVk = VirtualKeys.DefaultInventory[i],
                Enabled = starters[i] != 0,
            });
        }

        for (var i = 0; i < SkillSlots; i++)
            profile.Skills.Add(new KeyMap());

        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F2, Messages = { "-clear" } });
        return profile;
    }

    /// <summary>Older profiles (and hand-edited files) may have the wrong number of slots.</summary>
    public void NormaliseSlots()
    {
        while (Inventory.Count < InventorySlots)
            Inventory.Add(new KeyMap { ToVk = VirtualKeys.DefaultInventory[Inventory.Count] });
        while (Inventory.Count > InventorySlots)
            Inventory.RemoveAt(Inventory.Count - 1);

        while (Skills.Count < SkillSlots)
            Skills.Add(new KeyMap());
        while (Skills.Count > SkillSlots)
            Skills.RemoveAt(Skills.Count - 1);
    }
}
