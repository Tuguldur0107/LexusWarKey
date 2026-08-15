namespace LexusWarKey.Core;

/// <summary>One spell on the selected unit's command card: the ability, and the address of its
/// CCommandButton frame object.</summary>
public sealed record CommandCardSpell(AbilityInfo Ability, ulong ButtonAddr);

/// <summary>Reads the SELECTED unit's on-screen command card straight from memory, so the overlay
/// can list the player's own skills without them typing names.
///
/// When a unit is selected the game builds its command card as CCommandButton frame objects, each
/// starting with a fixed vtable pointer. Every such object holds, at +0x190, a data record whose
/// +0x04 is the ability rawcode. Find Game.dll's base, compute the vtable address, scan for it,
/// and decode each button's rawcode against the ability table.
///
/// The vtable RVA is 1.26a-specific (source: UnryzeC/MemHackAPI 12-APIMemoryFrameData.j, the
/// `PatchVersion == "1.26a"` block); if the build differs the scan finds nothing and the caller
/// falls back to naming skills. The structure offsets (+0x190, +0x04) are stable across the era.</summary>
public static class CommandCard
{
    /// <summary>CCommandButton vtable RVA off Game.dll, WC3 1.26a build 6401.</summary>
    public const uint VtableRva126a = 0x93EBC4;

    private const int DataOffset = 0x190;
    private const int RawcodeOffset = 0x04;

    /// <summary>The real spells on the command card (basic orders like move/stop/attack are
    /// dropped: their codes are not in the ability table). Deduped by ability id.</summary>
    public static IReadOnlyList<CommandCardSpell> ReadSpells(MemorySnapshot snap, ulong vtableAddr, AbilityData data)
    {
        var buttons = snap.RefsTo(new HashSet<uint> { (uint)vtableAddr }).Keys;

        var spells = new List<CommandCardSpell>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in buttons)
        {
            if (snap.Read32(obj + DataOffset) is not { } dataRec)
                continue;
            if (snap.Read32(dataRec + RawcodeOffset) is not { } raw)
                continue;

            foreach (var code in RawcodeStrings(raw))
            {
                if (data.ById.TryGetValue(code, out var ability))
                {
                    if (seen.Add(ability.Id))
                        spells.Add(new CommandCardSpell(ability, obj));
                    break;
                }
            }
        }
        return spells;
    }

    /// <summary>A rawcode dword decodes to a 4-char id; the byte order in memory is not fixed, so
    /// both the direct and reversed readings are offered and the caller keeps whichever the table
    /// knows.</summary>
    private static IEnumerable<string> RawcodeStrings(uint raw)
    {
        var b = new[] { (char)(raw & 0xFF), (char)((raw >> 8) & 0xFF), (char)((raw >> 16) & 0xFF), (char)((raw >> 24) & 0xFF) };
        var forward = new string(b);
        var reversed = new string(new[] { b[3], b[2], b[1], b[0] });
        yield return forward;
        yield return reversed;
    }
}
