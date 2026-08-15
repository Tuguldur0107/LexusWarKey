using LexusWarKey.Core;
using Xunit;

namespace LexusWarKey.Tests;

/// <summary>Hermetic tests for reading the command card from a fake address space: button objects
/// start with a vtable pointer and hold a data record at +0x190 whose +0x04 is the ability rawcode.</summary>
public class CommandCardTests
{
    private const ulong Base = 0x30000000;
    private const ulong Vtable = 0x0324EBC4;   // Game.dll base + 0x93EBC4, arbitrary here

    private sealed class Image
    {
        private readonly byte[] _buf;
        public Image(int size) => _buf = new byte[size];
        public void Put32(int off, uint v)
        {
            _buf[off] = (byte)v; _buf[off + 1] = (byte)(v >> 8);
            _buf[off + 2] = (byte)(v >> 16); _buf[off + 3] = (byte)(v >> 24);
        }
        public void PutStr(int off, string s) { foreach (var c in s) _buf[off++] = (byte)c; _buf[off] = 0; }
        public MemorySnapshot Snapshot() => new(new[] { (Base, _buf) });
    }

    /// <summary>Rawcode as it sits in memory: the little-endian dword whose reversed bytes spell
    /// the id, matching what the game stores and how ReadSpells decodes it.</summary>
    private static uint RawcodeLE(string id) =>
        (uint)((byte)id[3] | ((byte)id[2] << 8) | ((byte)id[1] << 16) | ((byte)id[0] << 24));

    private static AbilityData TableWith(params (string Id, char L, string Name)[] rows)
    {
        var csv = "id,in_w3a,hotkey,researchhotkey,unhotkey,buttonpos,name,tip\n";
        foreach (var (id, l, name) in rows)
            csv += $"{id},False,{l},,,\"0,1\",{name},t\n";
        return AbilityData.Parse(csv);
    }

    [Fact]
    public void Reads_spells_from_command_buttons_and_skips_basic_orders()
    {
        var img = new Image(0x2000);
        // Button 1 -> data at 0x800, rawcode 'A0R5' (a real spell)
        img.Put32(0x100, (uint)Vtable);
        img.Put32(0x100 + 0x190, (uint)(Base + 0x800));
        img.Put32(0x800 + 0x04, RawcodeLE("A0R5"));
        // Button 2 -> data at 0x900, rawcode 'AHer' (a hero-attribute button, not in the table)
        img.Put32(0x300, (uint)Vtable);
        img.Put32(0x300 + 0x190, (uint)(Base + 0x900));
        img.Put32(0x900 + 0x04, RawcodeLE("AHer"));

        var data = TableWith(("A0R5", 'R', "Soul Rip"));
        var spells = CommandCard.ReadSpells(img.Snapshot(), Vtable, data);

        var spell = Assert.Single(spells);
        Assert.Equal("A0R5", spell.Ability.Id);
        Assert.Equal("Soul Rip", spell.Ability.Name);
        Assert.Equal(Base + 0x100, spell.ButtonAddr);
    }

    [Fact]
    public void LiveSkills_lists_skills_with_cells_and_flags_duplicate_letters()
    {
        var img = new Image(0x4000);
        // Two command buttons, Soul Rip (R) and God's Strength (R) -> duplicate.
        img.Put32(0x100, (uint)Vtable);
        img.Put32(0x100 + 0x190, (uint)(Base + 0x800));
        img.Put32(0x800 + 0x04, RawcodeLE("A0R5"));
        img.Put32(0x120, (uint)Vtable);
        img.Put32(0x120 + 0x190, (uint)(Base + 0x820));
        img.Put32(0x820 + 0x04, RawcodeLE("A0WP"));

        // UI nodes so the hotkey cells resolve (name PString at +0xA8, hotkey ptr at +0x84).
        PutNode(img, node: 0x1000, name: "Soul Rip", letter: 'R', nameBytes: 0x1800, cell: 0x1A00, pcell: 0x1900);
        PutNode(img, node: 0x2000, name: "God's Strength", letter: 'R', nameBytes: 0x2800, cell: 0x2A00, pcell: 0x2900);

        var data = TableWith(("A0R5", 'R', "Soul Rip"), ("A0WP", 'R', "God's Strength"));
        // gameDllBase chosen so gameDllBase + VtableRva126a == Vtable (the value stored in the buttons).
        var set = LiveSkills.Read(img.Snapshot(), gameDllBase: Vtable - CommandCard.VtableRva126a, data);

        Assert.Equal(2, set.Skills.Count);
        Assert.Equal(new[] { 'R' }, set.Duplicates);
        Assert.Equal(Base + 0x1A00, set.Skills[0].CellAddr);   // Soul Rip cell
        // The shadowed one is the SECOND R in card order (God's Strength).
        var shadow = Assert.Single(set.Shadowed());
        Assert.Equal("God's Strength", shadow.Ability.Name);
        Assert.Equal(Base + 0x2A00, shadow.CellAddr);
    }

    private static void PutNode(Image img, int node, string name, char letter, int nameBytes, int cell, int pcell)
    {
        img.PutStr(nameBytes, name);
        img.Put32(pcell, (uint)(Base + (ulong)nameBytes));   // PString inner -> name bytes
        img.Put32(node + 0xA8, (uint)(Base + (ulong)pcell)); // node+0xA8 -> inner (2 hops)
        img.Put32(node + 0x84, (uint)(Base + (ulong)cell));  // node+0x84 -> cell
        img.Put32(cell, letter);
    }
}
