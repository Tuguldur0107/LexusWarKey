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

    /// <summary>CustomKeys mode: skill cells with a game letter (<see cref="KeyMap.ToVk"/>)
    /// SEND that letter instead of clicking the cell — no cursor, no click queue, spam-safe.
    ///
    /// Opt-in, off by default, because it only makes sense once the player has installed the
    /// generated CustomKeys.txt (which gives every LoD ability its cell's letter) and switched
    /// Custom Keyboard Shortcuts on in the game. With the file absent the letters would land in
    /// the game as dead keys, which looks exactly like the app being broken. A cell whose letter
    /// is empty still clicks — that stays the fallback for shadowed same-letter pairs.</summary>
    public bool UseGameLetters { get; set; }

    /// <summary>The letters the generated CustomKeys.txt assigns per card cell, indexed by slot
    /// (0-11; 0 = no letter). Middle row `- Q W G`, bottom row `R D F E` — the user's own Garena
    /// layout, and the layout baked into outputs/customkeys/CustomKeys.txt. The hidden top row
    /// and the middle row's first cell carry no letter.</summary>
    public static readonly int[] DefaultSlotLetters =
    {
        0, 0, 0, 0,
        0, 'Q', 'W', 'G',
        'R', 'D', 'F', 'E',
    };

    /// <summary>Fills each skill cell's game letter with the CustomKeys scheme default, leaving
    /// letters the user already set (or cleared deliberately? there is no way to tell — so only
    /// empty ones are touched, which makes this safe to run repeatedly).</summary>
    public void SeedSkillLetters()
    {
        for (var i = 0; i < Skills.Count && i < DefaultSlotLetters.Length; i++)
            if (Skills[i].ToVk == 0)
                Skills[i].ToVk = DefaultSlotLetters[i];
    }

    /// <summary>Undoes <see cref="SeedSkillLetters"/> when CustomKeys mode is switched off, so
    /// the profile returns to exactly its pre-mode state instead of carrying letters that the
    /// engine would then treat as plain remaps on an unlinked card. Only cells still holding
    /// their slot's DEFAULT are cleared: a hand-set letter is the user's data and survives the
    /// toggle — and a hand-set letter that happens to equal the default loses nothing, because
    /// re-enabling the mode re-seeds that same value.</summary>
    public void ClearSeededSkillLetters()
    {
        for (var i = 0; i < Skills.Count && i < DefaultSlotLetters.Length; i++)
            if (Skills[i].ToVk == DefaultSlotLetters[i])
                Skills[i].ToVk = 0;
    }

    /// <summary>Legacy setting from when posting messages was the primary click path. Kept so
    /// old profile files still deserialise; no longer read anywhere.</summary>
    public bool MoveCursorForClicks { get; set; }

    /// <summary>Post clicks to the game's window instead of moving the real cursor there.
    /// OFF, and off for everyone: this is deliberately a DIFFERENT property name from the
    /// "usePostedClicks" that v1.9.3-v1.9.5 wrote into every installed profile. Those files say
    /// true, the serialiser ignores members it does not know, and so an update lands every
    /// existing player back on the cursor — which is the entire point of renaming it rather than
    /// flipping the old default, since flipping a default reaches nobody who has ever saved.
    ///
    /// Why it is off. Three releases shipped this path and no ability has ever been observed to
    /// come out of it. The two claims on the record that it worked — "verified in a real match"
    /// (v1.9.3) and "it had at least worked every few presses" (v1.9.5) — are both contradicted
    /// by the log they were written beside: not one posted click exists before the v1.9.3 commit,
    /// and the 266 that follow arrive in mash bursts, twelve presses of one slot inside two
    /// seconds, which is what a player does when nothing is happening. The cursor path in the
    /// same log has 365 clicks across four days of real matches, over which the complaint was
    /// that skills were lost SOMETIMES.
    ///
    /// Why the reason previously given here was wrong. It claimed Warcraft is launched elevated
    /// and UIPI silently drops this app's messages. Warcraft 1.26a's manifest requests no
    /// elevation, no war3.exe path carries the RUNASADMIN compatibility flag, GameRanger's own
    /// manifest is asInvoker, and this account's interactive token is Medium integrity — so
    /// there is no integrity gap for UIPI to act on. The chat macros say the same thing from the
    /// other side: they reach the same window through SendInput, which UIPI governs by the same
    /// predicate, and they work every day. Telling the player to run as administrator was advice
    /// for a problem they do not have.
    ///
    /// It is kept, unreachable unless the file says so, because the question it was built to
    /// answer is still open and this is the only cheap way to ask it again.</summary>
    public bool PostClicksToGameWindow { get; set; }

    /// <summary>Milliseconds between telling the game where the pointer is and pressing the
    /// button, and how long the button is then held, for <see cref="PostClicksToGameWindow"/>.
    /// In the profile with no UI so they can be changed by editing the file and restarting.
    /// Neither of these numbers, nor any other, has yet been seen to cast anything; zero settle
    /// reproduces v1.9.3's back-to-back behaviour exactly.</summary>
    public int PostedSettleMs { get; set; } = 24;
    public int PostedHoldMs { get; set; } = 30;

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
