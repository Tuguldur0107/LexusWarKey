using System.Text;

namespace LexusWarKey.Core;

/// <summary>The authoritative hotkey cell of one live ability: write <see cref="CellAddr"/> to
/// change which key casts it. <see cref="Node"/> is the ability's UI-data node.</summary>
public sealed record HotkeyCell(string AbilityId, ulong Node, ulong CellAddr, char Letter);

/// <summary>Finds each skill's AUTHORITATIVE hotkey cell in a war3 memory snapshot.
///
/// Warcraft reads the cast hotkey through a double indirection off the ability's UI node:
/// <c>cell = *(node + 0x84)</c> is a pointer, <c>*(cell)</c> is the letter as an ASCII int. The
/// inline text record the game/editor loads is a cosmetic copy the input handler never reads,
/// which is why writing it did nothing and writing the cell works (verified in-game 2026-08-15).
///
/// <c>GetUINode(id)</c> is a game function an external tool cannot call, so the node is found the
/// other way round: from the ability's own NAME string, which sits at <c>node + 0xA8</c> as a
/// pointer (a PString, i.e. two hops to the char*). The name/hotkey offsets are not trusted
/// blindly - a few name offsets and both hop counts are tried, and only a layout whose
/// <c>+0x84</c> cell holds the RIGHT letter for several different skills at once is accepted.
/// That multi-skill agreement is what makes the match certain rather than a coincidence.
///
/// Structure offsets (+0x84 hotkey, +0xA8 name) are stable across the WC3 classic era; the
/// resolver never hardcodes a Game.dll RVA, so it does not depend on the exact patch build.
/// Source: UnryzeC/MemHackAPI 58-MemHackAbilityBaseAPI.j (DracoL1ch lineage).</summary>
public static class HotkeyResolver
{
    private const int HotOffset = 0x84;
    private static readonly int[] NameOffsets = { 0xA8, 0xAC, 0xA4, 0xB0, 0x9C, 0xB4 };

    // war3 is 32-bit; a real heap pointer sits above the image base and below the user-space end.
    private const uint MinPtr = 0x00400000;
    private const uint MaxPtr = 0x7FFF0000;

    /// <summary>The best layout found and the confirmed cell per ability. Empty cells means the
    /// names were not found in memory (wrong match/spelling) or the layout differs on this build.</summary>
    public sealed record Result(int NameOffset, int Hops, IReadOnlyDictionary<string, HotkeyCell> Cells)
    {
        public static readonly Result Empty = new(0, 0, new Dictionary<string, HotkeyCell>());
        public bool Found => Cells.Count > 0;
    }

    /// <summary>Locate the hotkey cell for each candidate ability. Pass every name-variant of the
    /// player's skills; only the live one matches, since each is checked against its own letter.</summary>
    public static Result ResolveCells(MemorySnapshot snap, IReadOnlyList<AbilityInfo> candidates)
    {
        if (candidates.Count == 0)
            return Result.Empty;

        // Phase 1: locate each name string once (variants share a name), then index it per ability.
        var addrsByName = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
        foreach (var name in candidates.Select(c => c.Name).Distinct())
        {
            var needle = Encoding.ASCII.GetBytes(name).Concat(new byte[] { 0 }).ToArray();
            addrsByName[name] = snap.FindBytes(needle).ToList();
        }

        var allNameBytes = addrsByName.Values.SelectMany(x => x).Select(a => (uint)a).ToHashSet();
        var ptrToName = snap.RefsTo(allNameBytes);                        // loc -> name-byte addr
        var ptrToPtr = snap.RefsTo(ptrToName.Keys.Select(k => (uint)k).ToHashSet());  // loc -> hop-1 loc

        // Invert to: name-byte addr -> locations that reference it, at one and two hops.
        var hop1 = new Dictionary<ulong, List<ulong>>();
        foreach (var (loc, val) in ptrToName)
            (hop1.TryGetValue(val, out var l) ? l : hop1[val] = new List<ulong>()).Add(loc);

        var hop2 = new Dictionary<ulong, List<ulong>>();
        foreach (var (loc, val) in ptrToPtr)
            if (ptrToName.TryGetValue(val, out var nb))   // val is a hop-1 loc; map back to its name
                (hop2.TryGetValue(nb, out var l) ? l : hop2[nb] = new List<ulong>()).Add(loc);

        // (nameOff, hops) -> confirmed cells whose doubly-dereferenced byte equals the letter.
        var byLayout = new Dictionary<(int NameOff, int Hops), List<HotkeyCell>>();
        foreach (var ab in candidates)
        {
            var want = (uint)ab.Letter;
            var nameAddrs = addrsByName[ab.Name];
            foreach (var (hops, table) in new[] { (1, hop1), (2, hop2) })
            {
                foreach (var nameFieldLoc in nameAddrs.SelectMany(nb => table.TryGetValue(nb, out var l) ? l : Enumerable.Empty<ulong>()))
                {
                    foreach (var nameOff in NameOffsets)
                    {
                        var node = nameFieldLoc - (ulong)nameOff;
                        var pcell = snap.Read32(node + HotOffset);
                        if (pcell is not { } cell || cell < MinPtr || cell >= MaxPtr)
                            continue;
                        if (snap.Read32(cell) is not { } val)
                            continue;
                        if ((val & 0xFF) == want && (val >> 8) == 0)
                        {
                            var key = (nameOff, hops);
                            (byLayout.TryGetValue(key, out var list) ? list : byLayout[key] = new List<HotkeyCell>())
                                .Add(new HotkeyCell(ab.Id, node, cell, ab.Letter));
                        }
                    }
                }
            }
        }

        if (byLayout.Count == 0)
            return Result.Empty;

        // The true layout explains the most distinct abilities; ties by tightest (smallest) name offset.
        var best = byLayout
            .OrderByDescending(kv => kv.Value.Select(c => c.AbilityId).Distinct().Count())
            .ThenBy(kv => kv.Key.NameOff)
            .First();

        var cells = new Dictionary<string, HotkeyCell>(StringComparer.Ordinal);
        foreach (var cell in best.Value)
            cells.TryAdd(cell.AbilityId, cell);   // one live node per ability

        return new Result(best.Key.NameOff, best.Key.Hops, cells);
    }
}
