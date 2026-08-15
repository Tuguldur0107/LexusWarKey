using System.IO;
using System.Reflection;

namespace LexusWarKey.Core;

/// <summary>One LoD ability: its 4-char id, the letter it answers to by default, its name, and its
/// command-card grid position (Col, Row) so the hero's skills can be put in visual order.</summary>
public sealed record AbilityInfo(string Id, char Letter, string Name, int Col = 99, int Row = 99);

/// <summary>The LoD ability table (bundled csv), used to recognise abilities in war3's memory.
///
/// The map ships hundreds of near-duplicate abilities: the same spell exists on several
/// letters (LoD reassigns them per slot), so a name like "Chaos Meteor" resolves to several
/// ids with different letters. Callers pass every variant to the memory search; only the copy
/// the hero actually holds exists as a live node, so the wrong-letter variants harmlessly
/// fail to match.</summary>
public sealed class AbilityData
{
    private readonly IReadOnlyDictionary<string, AbilityInfo> _byId;

    private AbilityData(IReadOnlyDictionary<string, AbilityInfo> byId) => _byId = byId;

    public IReadOnlyDictionary<string, AbilityInfo> ById => _byId;

    /// <summary>Abilities whose name contains <paramref name="term"/> (case-insensitive), or the
    /// single ability whose id is exactly <paramref name="term"/>. Only abilities with a real
    /// single-letter hotkey are returned, since the letterless ones cannot be searched for.</summary>
    public IReadOnlyList<AbilityInfo> Resolve(string term)
    {
        term = term.Trim();
        if (term.Length == 4 && _byId.TryGetValue(term, out var exact))
            return new[] { exact };

        return _byId.Values
            .Where(a => a.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Loads the table embedded in the app assembly.</summary>
    public static AbilityData LoadEmbedded()
    {
        var asm = typeof(AbilityData).Assembly;
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("lod_ability_table.csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("lod_ability_table.csv is not embedded in the assembly.");
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the csv text. Columns: id, in_w3a, hotkey, researchhotkey, unhotkey,
    /// buttonpos, name, tip. Only id/hotkey/name are used. Rows without a single alphabetic
    /// hotkey are skipped (they cannot be searched for by letter).</summary>
    public static AbilityData Parse(string csv)
    {
        var byId = new Dictionary<string, AbilityInfo>(StringComparer.Ordinal);
        var lines = SplitRecords(csv);
        var first = true;
        foreach (var fields in lines)
        {
            if (first) { first = false; continue; }   // header
            if (fields.Count < 7)
                continue;

            var id = fields[0].Trim();
            var hotkey = fields[2].Trim().Trim('"').Trim();
            // The map stores names wrapped in literal quotes (e.g. "Living Armor"); strip them so the
            // name matches the clean string the game keeps in memory - the resolver searches by it.
            var name = fields[6].Trim().Trim('"').Trim();
            if (id.Length != 4 || hotkey.Length != 1 || !char.IsLetter(hotkey[0]) || name.Length == 0)
                continue;

            var (col, row) = ParseButtonPos(fields[5]);

            // Keep the first row for an id; later duplicates of the same id are ignored.
            byId.TryAdd(id, new AbilityInfo(id, char.ToUpperInvariant(hotkey[0]), name, col, row));
        }
        return new AbilityData(byId);
    }

    /// <summary>"col,row" -> (col, row); anything unparseable sorts last at (99, 99).</summary>
    private static (int Col, int Row) ParseButtonPos(string raw)
    {
        var parts = raw.Trim().Trim('"').Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var col) && int.TryParse(parts[1], out var row))
            return (col, row);
        return (99, 99);
    }

    /// <summary>RFC-4180-ish record split: honours double-quoted fields, doubled "" escapes,
    /// and newlines inside quotes. Enough for this file, not a general csv library.</summary>
    private static List<List<string>> SplitRecords(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { /* swallow; \n ends the record */ }
            else if (c == '\n')
            {
                fields.Add(field.ToString()); field.Clear();
                records.Add(fields); fields = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }
        return records;
    }
}
