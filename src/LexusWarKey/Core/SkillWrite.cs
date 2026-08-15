namespace LexusWarKey.Core;

/// <summary>A skill detected on the command card: the ability, the letter it currently casts on, and
/// the letter the player assigned to it (0 if none). Its default letter is <c>Ability.Letter</c>.</summary>
public sealed record DetectedSkill(AbilityInfo Ability, char CurrentLetter, char DesiredLetter);

/// <summary>One hotkey write to apply: set the skill's cell to <see cref="DesiredLetter"/>.</summary>
public sealed record SkillWrite(string AbilityId, ulong CellAddr, char CurrentLetter, char DesiredLetter);

/// <summary>Decides, with no side effects, which skill hotkeys to rewrite. The player assigns a letter
/// to each ability by id; the app writes that letter onto the ability wherever it appears - no
/// position ordering, so the mapping never depends on how the game laid the card out.</summary>
public static class SkillWritePlanner
{
    /// <summary>`detected` is each command-card skill with the cell holding its hotkey and the letter
    /// currently there. `desiredById` is the player's assigned letter per ability id. Returns the
    /// per-skill view (sorted by name for a stable list) and the writes to apply (only where the
    /// assigned letter differs from what is currently there).</summary>
    public static (IReadOnlyList<DetectedSkill> Skills, IReadOnlyList<SkillWrite> Writes) Plan(
        IReadOnlyList<(AbilityInfo Ability, ulong Cell, char Current)> detected,
        IReadOnlyDictionary<string, char> desiredById)
    {
        var rows = new List<DetectedSkill>();
        var writes = new List<SkillWrite>();

        foreach (var d in detected.OrderBy(d => d.Ability.Name, StringComparer.OrdinalIgnoreCase))
        {
            var desired = desiredById.TryGetValue(d.Ability.Id, out var w) ? w : '\0';
            rows.Add(new DetectedSkill(d.Ability, d.Current, desired));

            // Only write when we know the cell and the letter actually needs changing.
            if (desired != '\0' && desired != d.Current && d.Cell != 0)
                writes.Add(new SkillWrite(d.Ability.Id, d.Cell, d.Current, desired));
        }
        return (rows, writes);
    }
}
