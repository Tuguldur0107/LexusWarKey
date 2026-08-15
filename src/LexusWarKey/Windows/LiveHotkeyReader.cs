using LexusWarKey.Core;

namespace LexusWarKey.Windows;

/// <summary>Bridges the pure skill logic to the live game: reads the player's current command card
/// and applies a hotkey write. Every call opens and closes its own handle, so nothing is held
/// between overlay uses and the game can come and go freely.</summary>
public sealed class LiveHotkeyReader
{
    private readonly AbilityData _abilities;

    public LiveHotkeyReader(AbilityData abilities) => _abilities = abilities;

    public enum Status { Ok, NotRunning, NeedAdmin, NoSkills, Failed }

    /// <summary>Reads the player's live skills. On anything other than <see cref="Status.Ok"/> the
    /// set is empty and the status says why (game closed, not elevated, no unit selected, …).</summary>
    public (Status Status, LiveSkillSet Skills) Read()
    {
        using var mem = War3Memory.Open(out var err);
        if (mem is null)
            return (Map(err), LiveSkillSet.Empty);

        var gameDll = mem.GetModuleBase("Game.dll");
        if (gameDll is not { } baseAddr)
            return (Status.NoSkills, LiveSkillSet.Empty);

        var set = LiveSkills.Read(mem.Snapshot(), baseAddr, _abilities);
        return set.Skills.Count == 0
            ? (Status.NoSkills, LiveSkillSet.Empty)
            : (Status.Ok, set);
    }

    /// <summary>Like <see cref="Read"/> but also returns every command-card spell (abilities, which
    /// stay on the card even after a skill is re-lettered). The auto-fixer needs the full card to
    /// notice a hero/match change and to order the slots, and the resolved skills to plan writes.</summary>
    public (Status Status, IReadOnlyList<AbilityInfo> Card, LiveSkillSet Skills) ReadDetailed()
    {
        using var mem = War3Memory.Open(out var err);
        if (mem is null)
            return (Map(err), Array.Empty<AbilityInfo>(), LiveSkillSet.Empty);

        var gameDll = mem.GetModuleBase("Game.dll");
        if (gameDll is not { } baseAddr)
            return (Status.NoSkills, Array.Empty<AbilityInfo>(), LiveSkillSet.Empty);

        var snap = mem.Snapshot();
        var vtable = baseAddr + CommandCard.VtableRva126a;
        var card = CommandCard.ReadSpells(snap, vtable, _abilities);
        if (card.Count == 0)
            return (Status.NoSkills, Array.Empty<AbilityInfo>(), LiveSkillSet.Empty);

        var resolved = HotkeyResolver.ResolveCells(snap, card.Select(s => s.Ability).ToList());
        var skills = new List<LiveSkill>();
        foreach (var spell in card)
            if (resolved.Cells.TryGetValue(spell.Ability.Id, out var cell))
                skills.Add(new LiveSkill(spell.Ability, cell.CellAddr, cell.Letter));

        var duplicates = skills.GroupBy(s => s.Letter).Where(g => g.Count() > 1)
            .Select(g => g.Key).OrderBy(l => l).ToList();
        var abilities = card.Select(s => s.Ability).ToList();
        return (Status.Ok, abilities, new LiveSkillSet(skills, duplicates));
    }

    /// <summary>Applies one guarded hotkey write. Returns false (with a reason) if the game closed,
    /// admin is missing, or the cell no longer holds the expected letter.</summary>
    public bool Apply(HotkeyWrite write, out string? error)
    {
        error = null;
        using var mem = War3Memory.Open(out var err);
        if (mem is null)
        {
            error = err == War3Memory.OpenError.AccessDenied ? "Run as administrator" : "Warcraft is not running";
            return false;
        }

        var result = mem.SetHotkey(write.CellAddr, write.NewLetter, out _);
        if (result == War3Memory.WriteResult.Ok)
            return true;

        error = result switch
        {
            War3Memory.WriteResult.Unreadable => "The skill's hotkey moved - reopen the overlay",
            War3Memory.WriteResult.NotALetter => "New key must be a letter",
            _ => "Write failed",
        };
        return false;
    }

    private static Status Map(War3Memory.OpenError err) => err switch
    {
        War3Memory.OpenError.NotRunning => Status.NotRunning,
        War3Memory.OpenError.AccessDenied => Status.NeedAdmin,
        _ => Status.Failed,
    };
}
