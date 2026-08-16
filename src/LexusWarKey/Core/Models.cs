using System.Text.Json.Serialization;

namespace LexusWarKey.Core;

/// <summary>One key translation: press <see cref="FromVk"/>, the game receives <see cref="ToVk"/>.
/// Strictly one key in, one key out.</summary>
public sealed class KeyMap
{
    public int FromVk { get; set; }
    public int ToVk { get; set; }
    public bool Enabled { get; set; } = true;

    [JsonIgnore] public bool IsUsable => Enabled && FromVk != 0 && ToVk != 0 && FromVk != ToVk;
    [JsonIgnore] public bool ClaimsKey => Enabled && FromVk != 0;
}

/// <summary>One QuickChat slot: a trigger key and one or more messages. Pressing the key sends every
/// message in order (each as its own all-chat line).</summary>
public sealed class ChatMacro
{
    public int HotkeyVk { get; set; }

    /// <summary>The messages this key sends, in order. Empty/whitespace lines are ignored.</summary>
    public List<string> Messages { get; set; } = new();

    public bool Enabled { get; set; } = true;

    /// <summary>Legacy option from older builds. QuickChat always sends to the normal all-chat path.</summary>
    public bool AlliesOnly { get; set; }

    /// <summary>The non-empty messages actually sent, trimmed and in order.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> UsableMessages =>
        Messages.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();

    /// <summary>First message — kept for display and old callers.</summary>
    [JsonIgnore]
    public string Message
    {
        get => Messages.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "";
        set
        {
            Messages.Clear();
            if (!string.IsNullOrWhiteSpace(value))
                Messages.Add(value.Trim());
        }
    }

    [JsonIgnore] public bool IsUsable => Enabled && HotkeyVk != 0 && UsableMessages.Count > 0;

    public void Normalise()
    {
        Messages ??= new();
        // Keep every non-empty message (trimmed), preserving order and how many there are.
        Messages = Messages.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList();
        Enabled = true;
        AlliesOnly = false;
    }
}

public sealed class WarKeyProfile
{
    /// <summary>The 4x2 skill grid. Each cell says: when I press FromVk, send the game ToVk,
    /// the letter that ability actually has this match.</summary>
    public List<KeyMap> Skills { get; set; } = new();

    /// <summary>The 2x3 item-inventory grid. Same shape as a skill map: press FromVk, the game
    /// receives ToVk (the inventory slot's own hotkey). Six slots, matching the hero inventory.</summary>
    public List<KeyMap> Inventory { get; set; } = new();

    /// <summary>The hotkey letter the player wants for each ability, keyed by its 4-char id. The app
    /// writes these onto the matching skills whenever they appear on the command card, so an
    /// assignment made once ("Soul Rip -> Q") keeps applying every match that skill shows up.</summary>
    public Dictionary<string, string> SkillLetters { get; set; } = new();

    /// <summary>Exactly two fixed QuickChat slots.</summary>
    public List<ChatMacro> ChatMacros { get; set; } = new();

    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }

    public bool Enabled { get; set; } = true;

    public const int SkillColumns = 4;
    public const int SkillRows = 2;
    public const int SkillSlots = SkillColumns * SkillRows;

    // The hero inventory is two columns wide and three rows tall.
    public const int InventoryColumns = 2;
    public const int InventoryRows = 3;
    public const int InventorySlots = InventoryColumns * InventoryRows;

    // DotA's inventory hotkeys sit on the numpad in the same 2x3 shape (top-left 7, then 8 / 4 5 /
    // 1 2), so each item slot's game key is fixed. The player only sets their own key on top of these.
    public static readonly int[] DefaultInventoryKeys =
    {
        VirtualKeys.NumPad7, VirtualKeys.NumPad8,
        VirtualKeys.NumPad4, VirtualKeys.NumPad5,
        VirtualKeys.NumPad1, VirtualKeys.NumPad2,
    };

    public const int QuickChatSlots = 2;

    /// <summary>Frees a physical key from everywhere - any skill using that letter and any inventory
    /// slot triggered by it - so a key is only ever bound in one place across both grids.</summary>
    private void FreeKey(int vk)
    {
        var letter = char.ToUpperInvariant((char)vk).ToString();
        foreach (var id in SkillLetters.Where(kv => string.Equals(kv.Value, letter, StringComparison.OrdinalIgnoreCase))
                     .Select(kv => kv.Key).ToList())
            SkillLetters.Remove(id);
        foreach (var slot in Inventory.Where(m => m.FromVk == vk))
        {
            slot.FromVk = 0;
            slot.Enabled = false;
        }
    }

    /// <summary>Assigns a letter to a skill, keeping it unique across skills AND inventory.</summary>
    public void SetSkillLetter(string abilityId, string letter)
    {
        if (letter.Length == 1)
            FreeKey(char.ToUpperInvariant(letter[0]));
        SkillLetters[abilityId] = letter;
    }

    public void ClearSkillLetter(string abilityId) => SkillLetters.Remove(abilityId);

    /// <summary>Binds a trigger key to an inventory slot, keeping it unique across inventory AND
    /// skills (the same physical key can't cast a skill and use an item at once).</summary>
    public void SetInventoryKey(int slotIndex, int vk)
    {
        if (slotIndex < 0 || slotIndex >= Inventory.Count)
            return;
        FreeKey(vk);
        Inventory[slotIndex].FromVk = vk;
        Inventory[slotIndex].Enabled = true;
    }

    public static WarKeyProfile CreateDefault()
    {
        var profile = new WarKeyProfile();

        for (var i = 0; i < SkillSlots; i++)
            profile.Skills.Add(new KeyMap());
        for (var i = 0; i < InventorySlots; i++)
            profile.Inventory.Add(new KeyMap { ToVk = DefaultInventoryKeys[i] });

        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F2 });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F3 });
        return profile;
    }

    public void NormaliseSlots()
    {
        Skills ??= new();
        Inventory ??= new();
        ChatMacros ??= new();
        SkillLetters ??= new();
        Skills = Skills.Select(map => map ?? new KeyMap()).ToList();
        Inventory = Inventory.Select(map => map ?? new KeyMap()).ToList();
        ChatMacros = ChatMacros.Select(macro => macro ?? new ChatMacro()).ToList();

        // Older profiles stored a 4x3 command card. The top row was
        // Move/Stop/Hold/Attack; keep only the lower two rows users actually bind.
        //
        // Their target keys do NOT survive. In those builds ToVk held a letter from the
        // generated CustomKeys.txt scheme, which assumed that file was installed in the game
        // folder; nothing generates or installs it any more, so those letters now point at
        // whatever abilities happen to use them. Carrying them over would leave a grid that
        // looks fully configured and casts the wrong ability on the first match after the
        // update. Clearing them costs one deliberate re-bind and cannot cast anything wrong.
        if (Skills.Count == 12)
        {
            Skills = Skills.Skip(4).ToList();
            foreach (var map in Skills)
                map.ToVk = 0;
        }

        while (Skills.Count < SkillSlots)
            Skills.Add(new KeyMap());
        while (Skills.Count > SkillSlots)
            Skills.RemoveAt(Skills.Count - 1);

        foreach (var map in Skills)
            if (map.FromVk == 0)
                map.Enabled = false;

        // Inventory is new; old profiles have none, so pad to the six item slots.
        while (Inventory.Count < InventorySlots)
            Inventory.Add(new KeyMap());
        while (Inventory.Count > InventorySlots)
            Inventory.RemoveAt(Inventory.Count - 1);

        for (var i = 0; i < Inventory.Count; i++)
        {
            // Each item slot's game key is fixed to its numpad hotkey; fill it in where unset so the
            // player only has to add their own key.
            if (Inventory[i].ToVk == 0)
                Inventory[i].ToVk = DefaultInventoryKeys[i];
            if (Inventory[i].FromVk == 0)
                Inventory[i].Enabled = false;
        }

        // QuickChat is an open list now (add as many as you like). Seed the first two on a brand-new
        // profile, but never trim what the player added.
        if (ChatMacros.Count == 0)
        {
            ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F2 });
            ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F3 });
        }
        foreach (var macro in ChatMacros)
            macro.Normalise();
    }
}
