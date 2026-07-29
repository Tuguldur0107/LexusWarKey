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
    /// <summary>False = every player sees it, enemies included (Shift+Enter in Warcraft III);
    /// true = the player's own team only (Ctrl+Enter).</summary>
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

    /// <summary>Always true. Kept so old profile files still deserialise: the skill grid never
    /// offered a target key, so the "false" branch left every skill binding inert — a switch
    /// with no working position. NormaliseSlots forces it back on.</summary>
    public bool SkillsUsePosition { get; set; } = true;

    /// <summary>Legacy setting from when posting messages was the primary click path. Kept so
    /// old profile files still deserialise; no longer read anywhere.</summary>
    public bool MoveCursorForClicks { get; set; }

    /// <summary>Post clicks to the game's window instead of moving the real cursor there. ON by
    /// default: the player will not accept the cursor being taken, however briefly.
    ///
    /// This needs the app to be running as administrator, and that is not a preference. Warcraft
    /// here is launched elevated, and Windows will not let a normal-privilege process send window
    /// messages to an elevated one — UIPI drops them without a word, so PostMessage returns
    /// success, the log fills with clicks that were never delivered, and not one ability comes
    /// out. The cursor path was unaffected because injected cursor movement goes through the
    /// session input queue rather than the game's message queue.</summary>
    public bool UsePostedClicks { get; set; } = true;

    /// <summary>Where the user dragged the in-game overlay to; null = default corner.</summary>
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }

    /// <summary>Cell size the user stretched the overlay grid to, so it reopens exactly the
    /// size that matched their command card; null = the built-in default.</summary>
    public double? OverlayCellWidth { get; set; }
    public double? OverlayCellHeight { get; set; }

    /// <summary>The activation code from TierBot's /warkey command, kept so it survives
    /// restarts. Validated on every launch — expiry is what ends access for ex-members.</summary>
    public string? ActivationToken { get; set; }

    /// <summary>Null until the app has asked once whether it should start with Windows.</summary>
    public bool? StartWithWindows { get; set; }

    /// <summary>When true a new release is downloaded and installed on startup without asking.
    /// Off by default: silently replacing a running executable is something the user opts into.</summary>
    public bool AutoInstallUpdates { get; set; }

    /// <summary>Closing the window hides the app to the notification area instead of quitting,
    /// so the remapper keeps working without occupying the taskbar.</summary>
    public bool MinimiseToTray { get; set; } = true;

    /// <summary>Remapping only acts while Warcraft III has focus — never in other programs.</summary>
    public bool OnlyWhenGameFocused { get; set; } = true;
    public bool Enabled { get; set; } = true;

    /// <summary>Warcraft's inventory is a 2x3 grid and its command card is 4x3, so the UI
    /// mirrors those shapes exactly instead of showing a flat list.</summary>
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

    /// <summary>Older profiles (and hand-edited files) may have the wrong number of slots, or
    /// slots left "enabled" with no key — which reads as configured but does nothing.</summary>
    public void NormaliseSlots()
    {
        // A hand-edited file with "inventory": null deserialises straight over these initialisers,
        // and the null then crashes startup every single launch — unrecoverably, since the file
        // is never rewritten. Heal first, ask questions later.
        Inventory ??= new();
        Skills ??= new();
        ChatMacros ??= new();
        CommandCard ??= new();
        foreach (var macro in ChatMacros)
            macro.Messages ??= new();

        // Profiles from before v1.2 stored 8 skill slots (a 4x2 model of what is really a 4x3
        // card). Those calibrations spanned the full card, so old row 0 landed on the real top
        // row and old row 1 on the real bottom row. Reseat each binding on the slot it was
        // actually clicking, rather than on the slot that shares its number.
        if (Skills.Count == 8 && SkillSlots == 12)
        {
            var reseated = Enumerable.Range(0, SkillSlots).Select(_ => new KeyMap()).ToList();
            for (var i = 0; i < 8; i++)
                reseated[i < 4 ? i : i + 4] = Skills[i];
            Skills = reseated;
        }

        // Several enabled macros on one key read as "this key sends all of these", but only the
        // first was ever matched — the rest failed invisibly. Fold them into one macro in the
        // order they were created, which is what the user was trying to build all along.
        var mergedMacros = new List<ChatMacro>();
        foreach (var macro in ChatMacros)
        {
            var twin = macro.Enabled && macro.HotkeyVk != 0
                ? mergedMacros.FirstOrDefault(m => m.Enabled && m.HotkeyVk == macro.HotkeyVk)
                : null;
            if (twin is null)
                mergedMacros.Add(macro);
            else
                twin.Messages.AddRange(macro.Messages);
        }
        ChatMacros = mergedMacros;

        SkillsUsePosition = true;

        // The card's top row is Move/Stop/Hold/Attack and is no longer shown, so a binding
        // left there could never be seen or removed again. Clear it rather than leave a key
        // silently claimed by a cell that does not exist in the UI.
        for (var i = 0; i < CommandCard.FirstBindableSlot && i < Skills.Count; i++)
        {
            Skills[i].FromVk = 0;
            Skills[i].ToVk = 0;
            Skills[i].Enabled = false;
        }

        // Hand-dragged ring positions carry a few pixels of tremor each; the card they were
        // aimed at is a perfectly even grid. Snap once on load so they stay tidy forever.
        CommandCard.TidyOverrides();

        foreach (var map in Inventory.Concat(Skills))
            if (map.FromVk == 0)
                map.Enabled = false;

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
