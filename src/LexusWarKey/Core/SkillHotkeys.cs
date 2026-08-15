namespace LexusWarKey.Core;

/// <summary>One skill the player named, resolved against live memory. <see cref="Ability"/> and
/// <see cref="CellAddr"/> are null when no live node matched (wrong name, or not on this hero).</summary>
public sealed record ConfirmedSkill(string Term, AbilityInfo? Ability, ulong? CellAddr, char? Letter)
{
    public bool Resolved => Ability is not null && CellAddr is not null;
}

/// <summary>Ties the ability table to the memory resolver: turns the skills the player names into
/// their live hotkey cells and flags duplicate letters (the ones Warcraft cannot all reach).</summary>
public static class SkillHotkeys
{
    /// <summary>For each term the player typed, the live variant memory confirmed (if any).</summary>
    public static IReadOnlyList<ConfirmedSkill> Confirm(
        MemorySnapshot snap, AbilityData data, IReadOnlyList<string> terms)
    {
        // Every variant of every term, deduped by id: only the live one will match memory.
        var perTerm = new List<(string Term, List<AbilityInfo> Variants)>();
        var candidates = new List<AbilityInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            var variants = data.Resolve(term);
            perTerm.Add((term, variants.ToList()));
            foreach (var v in variants)
                if (seen.Add(v.Id))
                    candidates.Add(v);
        }

        var result = HotkeyResolver.ResolveCells(snap, candidates);

        var confirmed = new List<ConfirmedSkill>();
        foreach (var (term, variants) in perTerm)
        {
            var live = variants.FirstOrDefault(v => result.Cells.ContainsKey(v.Id));
            if (live is not null && result.Cells.TryGetValue(live.Id, out var cell))
                confirmed.Add(new ConfirmedSkill(term, live, cell.CellAddr, live.Letter));
            else
                confirmed.Add(new ConfirmedSkill(term, null, null, null));
        }
        return confirmed;
    }

    /// <summary>Letters carried by more than one confirmed skill. Warcraft answers only the first
    /// skill on each, so the rest are the shadowed skills this app exists to rescue.</summary>
    public static IReadOnlyList<char> DuplicateLetters(IEnumerable<ConfirmedSkill> confirmed) =>
        confirmed
            .Where(c => c.Resolved)
            .GroupBy(c => c.Letter!.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(l => l)
            .ToList();
}
