using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Hermetic tests for the memory hotkey resolver. A fake war3 address space is built in a
/// byte buffer with known UI-node layouts; nothing touches a real process. Mirrors the python
/// probe's synthetic test so both implementations are checked against the same shapes.</summary>
public class HotkeyMemoryTests
{
    private const ulong Base = 0x20000000;

    private sealed class Image
    {
        private readonly byte[] _buf;
        public Image(int size) => _buf = new byte[size];

        public void Put32(int off, uint val)
        {
            _buf[off] = (byte)val; _buf[off + 1] = (byte)(val >> 8);
            _buf[off + 2] = (byte)(val >> 16); _buf[off + 3] = (byte)(val >> 24);
        }

        public void PutStr(int off, string s)
        {
            foreach (var c in s) _buf[off++] = (byte)c;
            _buf[off] = 0;
        }

        public MemorySnapshot Snapshot() => new(new[] { (Base, _buf) });
    }

    /// <summary>Places a UI node: name field -> name string (direct=1 hop, or PString=2 hops),
    /// hotkey pointer -> cell holding the letter.</summary>
    private static void PutNode(Image img, int node, int nameOff, string name, char letter,
                                int nameBytesOff, int cellOff, bool pstring, int pstringCellOff = 0)
    {
        img.PutStr(nameBytesOff, name);
        if (pstring)
        {
            img.Put32(pstringCellOff, (uint)(Base + (ulong)nameBytesOff));   // C -> name bytes
            img.Put32(node + nameOff, (uint)(Base + (ulong)pstringCellOff)); // node+nameOff -> C
        }
        else
        {
            img.Put32(node + nameOff, (uint)(Base + (ulong)nameBytesOff));   // node+nameOff -> name bytes
        }
        img.Put32(node + 0x84, (uint)(Base + (ulong)cellOff));              // node+0x84 -> cell
        img.Put32(cellOff, letter);                                        // cell -> letter int
    }

    private static AbilityInfo Ab(string id, char letter, string name) => new(id, letter, name);

    [Fact]
    public void Finds_cells_for_the_real_pstring_layout()
    {
        // The live game uses one uniform layout for every ability (confirmed in-game: name at
        // +0xA8 as a PString, 2 hops). ResolveCells returns the single best layout, so both
        // skills must share it - which they do, because all real nodes look alike.
        var img = new Image(0x4000);
        PutNode(img, 0x100, 0xA8, "Chaos Meteor", 'D', 0x800, 0x0A00, pstring: true, pstringCellOff: 0x900);
        PutNode(img, 0x1400, 0xA8, "Tornado", 'X', 0x1800, 0x1A00, pstring: true, pstringCellOff: 0x1900);

        var result = HotkeyResolver.ResolveCells(img.Snapshot(), new[]
        {
            Ab("Z602", 'D', "Chaos Meteor"), Ab("Z608", 'X', "Tornado"),
        });

        Assert.True(result.Found);
        Assert.Equal(2, result.Hops);
        Assert.Equal(0xA8, result.NameOffset);
        Assert.Equal(Base + 0x0A00, result.Cells["Z602"].CellAddr);
        Assert.Equal(Base + 0x100, result.Cells["Z602"].Node);
        Assert.Equal(Base + 0x1A00, result.Cells["Z608"].CellAddr);
    }

    [Fact]
    public void Picks_the_live_variant_and_rejects_the_wrong_letter_one()
    {
        var img = new Image(0x4000);
        // Live "Split Earth" node holds letter 'T'.
        PutNode(img, 0x2000, 0xA8, "Split Earth", 'T', 0x2800, 0x2A00, pstring: false);

        var result = HotkeyResolver.ResolveCells(img.Snapshot(), new[]
        {
            Ab("A0AA", 'T', "Split Earth"),   // live variant
            Ab("A0BB", 'F', "Split Earth"),   // dead variant: same name, wrong letter
        });

        Assert.True(result.Cells.ContainsKey("A0AA"));
        Assert.False(result.Cells.ContainsKey("A0BB"));
        Assert.Equal(Base + 0x2A00, result.Cells["A0AA"].CellAddr);
    }

    [Fact]
    public void Returns_empty_when_no_node_matches()
    {
        var img = new Image(0x1000);   // nothing placed
        var result = HotkeyResolver.ResolveCells(img.Snapshot(),
            new[] { Ab("Z602", 'D', "Chaos Meteor") });
        Assert.False(result.Found);
    }

    [Fact]
    public void Confirm_maps_terms_to_live_cells_and_flags_duplicate_letters()
    {
        var img = new Image(0x4000);
        // Three skills, two on 'R' -> a real duplicate.
        PutNode(img, 0x100, 0xA8, "Elune's Arrow", 'R', 0x800, 0x0A00, pstring: false);
        PutNode(img, 0x400, 0xA8, "Arc Lightning", 'C', 0xB00, 0x0D00, pstring: false);
        PutNode(img, 0x1000, 0xA8, "God's Strength", 'R', 0x1800, 0x1A00, pstring: false);

        var data = AbilityData.Parse(
            "id,in_w3a,hotkey,researchhotkey,unhotkey,buttonpos,name,tip\n" +
            "A0AA,False,R,,,\"0,1\",Elune's Arrow,t\n" +
            "A0BB,False,C,,,\"0,1\",Arc Lightning,t\n" +
            "A0CC,False,R,,,\"0,1\",God's Strength,t\n");

        var confirmed = SkillHotkeys.Confirm(img.Snapshot(), data,
            new[] { "Elune", "Arc Lightning", "God's Strength" });

        Assert.All(confirmed, c => Assert.True(c.Resolved, $"{c.Term} not resolved"));
        Assert.Equal(Base + 0x0A00, confirmed[0].CellAddr);
        Assert.Equal('R', confirmed[0].Letter);

        var dups = SkillHotkeys.DuplicateLetters(confirmed);
        Assert.Equal(new[] { 'R' }, dups);
    }

    [Fact]
    public void Snapshot_read32_and_refsto_work()
    {
        var img = new Image(0x100);
        img.Put32(0x10, 0xDEADBEEF);
        img.Put32(0x20, (uint)(Base + 0x10));   // a pointer to 0x10
        var snap = img.Snapshot();

        Assert.Equal(0xDEADBEEFu, snap.Read32(Base + 0x10));
        Assert.Null(snap.Read32(Base + 0x1000));   // out of range

        var refs = snap.RefsTo(new HashSet<uint> { (uint)(Base + 0x10) });
        Assert.True(refs.ContainsKey(Base + 0x20));
    }
}
