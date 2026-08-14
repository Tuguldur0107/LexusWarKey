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

/// <summary>One fixed QuickChat slot: one trigger key, one message.</summary>
public sealed class ChatMacro
{
    public int HotkeyVk { get; set; }

    /// <summary>Kept as a list for old profile compatibility. The simplified app uses only
    /// the first non-empty line and trims every slot back to one message on load/save.</summary>
    public List<string> Messages { get; set; } = new();

    public bool Enabled { get; set; } = true;

    /// <summary>Legacy option from older builds. Simplified QuickChat always sends the visible
    /// message to the normal all-chat path.</summary>
    public bool AlliesOnly { get; set; }

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

    [JsonIgnore] public bool IsUsable => Enabled && HotkeyVk != 0 && !string.IsNullOrWhiteSpace(Message);

    public void Normalise()
    {
        Messages ??= new();
        Message = Message;
        Enabled = true;
        AlliesOnly = false;
    }
}

public sealed class WarKeyProfile
{
    /// <summary>The 4x2 skill grid. Each cell says: when I press FromVk, send the game ToVk,
    /// the letter that ability actually has this match.</summary>
    public List<KeyMap> Skills { get; set; } = new();

    /// <summary>Exactly two fixed QuickChat slots.</summary>
    public List<ChatMacro> ChatMacros { get; set; } = new();

    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }

    public bool Enabled { get; set; } = true;

    public const int SkillColumns = 4;
    public const int SkillRows = 2;
    public const int SkillSlots = SkillColumns * SkillRows;
    public const int QuickChatSlots = 2;

    public static WarKeyProfile CreateDefault()
    {
        var profile = new WarKeyProfile();

        for (var i = 0; i < SkillSlots; i++)
            profile.Skills.Add(new KeyMap());

        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F2 });
        profile.ChatMacros.Add(new ChatMacro { HotkeyVk = VirtualKeys.F3 });
        return profile;
    }

    public void NormaliseSlots()
    {
        Skills ??= new();
        ChatMacros ??= new();
        Skills = Skills.Select(map => map ?? new KeyMap()).ToList();
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

        while (ChatMacros.Count < QuickChatSlots)
            ChatMacros.Add(new ChatMacro { HotkeyVk = ChatMacros.Count == 0 ? VirtualKeys.F2 : VirtualKeys.F3 });
        while (ChatMacros.Count > QuickChatSlots)
            ChatMacros.RemoveAt(ChatMacros.Count - 1);
        foreach (var macro in ChatMacros)
            macro.Normalise();
    }
}
