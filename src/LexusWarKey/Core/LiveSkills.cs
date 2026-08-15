namespace LexusWarKey.Core;

/// <summary>One of the player's live skills: the ability, its authoritative hotkey cell to write,
/// and the letter it currently answers to.</summary>
public sealed record LiveSkill(AbilityInfo Ability, ulong CellAddr, char Letter);

/// <summary>A change to apply to the live game: write <see cref="NewLetter"/> into the skill's
/// hotkey cell so that key now casts it.</summary>
public sealed record HotkeyWrite(ulong CellAddr, char NewLetter, string SkillName, char OldLetter);

/// <summary>The player's current on-screen skills with their writable hotkey cells and any duplicate
/// letters. This is the whole data model the Ctrl+F6 overlay needs to fix a shadowed skill.</summary>
public sealed record LiveSkillSet(IReadOnlyList<LiveSkill> Skills, IReadOnlyList<char> Duplicates)
{
    public static readonly LiveSkillSet Empty = new(Array.Empty<LiveSkill>(), Array.Empty<char>());

    /// <summary>The skills sharing a duplicated letter, in card order. The first on each letter is
    /// the one Warcraft reaches; the rest are shadowed and need a new key.</summary>
    public IReadOnlyList<LiveSkill> Shadowed()
    {
        var firstSeen = new HashSet<char>();
        var shadowed = new List<LiveSkill>();
        foreach (var s in Skills)
        {
            if (!Duplicates.Contains(s.Letter))
                continue;
            if (!firstSeen.Add(s.Letter))
                shadowed.Add(s);
        }
        return shadowed;
    }
}

/// <summary>Turns a war3 memory snapshot into the player's live skill set: read the command card,
/// resolve each spell's hotkey cell, flag duplicate letters.</summary>
public static class LiveSkills
{
    public static LiveSkillSet Read(MemorySnapshot snap, ulong gameDllBase, AbilityData data)
    {
        var vtable = gameDllBase + CommandCard.VtableRva126a;
        var spells = CommandCard.ReadSpells(snap, vtable, data);
        if (spells.Count == 0)
            return LiveSkillSet.Empty;

        var resolved = HotkeyResolver.ResolveCells(snap, spells.Select(s => s.Ability).ToList());

        // Keep command-card order, and only skills whose writable cell we actually found.
        var skills = new List<LiveSkill>();
        foreach (var spell in spells)
            if (resolved.Cells.TryGetValue(spell.Ability.Id, out var cell))
                skills.Add(new LiveSkill(spell.Ability, cell.CellAddr, cell.Letter));

        var duplicates = skills
            .GroupBy(s => s.Letter)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(l => l)
            .ToList();

        return new LiveSkillSet(skills, duplicates);
    }
}
