namespace LexusWarKey.Core;

/// <summary>A read-only copy of a process's committed memory regions, as (base, bytes) blobs.
///
/// The hotkey search chases pointers across the whole address space, so the game is read once
/// into one of these and every hop reads from it. Keeping it as a plain abstraction (no Win32)
/// means the resolver can be tested against a hand-built address space with no live process.</summary>
public sealed class MemorySnapshot
{
    /// <summary>Regions sorted by base address; each is a contiguous byte blob.</summary>
    private readonly List<(ulong Base, byte[] Data)> _regions;

    public MemorySnapshot(IEnumerable<(ulong Base, byte[] Data)> regions)
    {
        _regions = regions.Where(r => r.Data.Length >= 4).OrderBy(r => r.Base).ToList();
    }

    public IReadOnlyList<(ulong Base, byte[] Data)> Regions => _regions;

    public long TotalBytes => _regions.Sum(r => (long)r.Data.Length);

    /// <summary>*(uint32*)addr, or null if the 4 bytes are not fully inside one region.</summary>
    public uint? Read32(ulong addr)
    {
        foreach (var (baseAddr, data) in _regions)
        {
            if (addr < baseAddr)
                break;   // regions are sorted; no later region starts lower
            if (addr <= baseAddr + (ulong)data.Length - 4)
            {
                var off = (int)(addr - baseAddr);
                return (uint)(data[off] | (data[off + 1] << 8) | (data[off + 2] << 16) | (data[off + 3] << 24));
            }
        }
        return null;
    }

    /// <summary>Every address at which <paramref name="needle"/> occurs, capped per region so a
    /// pathological match count cannot blow up.</summary>
    public IEnumerable<ulong> FindBytes(byte[] needle, int capPerRegion = 64)
    {
        foreach (var (baseAddr, data) in _regions)
        {
            var found = 0;
            var at = IndexOf(data, needle, 0);
            while (at >= 0 && found < capPerRegion)
            {
                yield return baseAddr + (ulong)at;
                found++;
                at = IndexOf(data, needle, at + 1);
            }
        }
    }

    /// <summary>For every dword in memory whose value is in <paramref name="targets"/>, map the
    /// LOCATION holding it to that value. This walks one pointer hop backwards: a location Q with
    /// value T means "something at Q points at T".</summary>
    public Dictionary<ulong, uint> RefsTo(IReadOnlySet<uint> targets)
    {
        var result = new Dictionary<ulong, uint>();
        if (targets.Count == 0)
            return result;

        foreach (var (baseAddr, data) in _regions)
        {
            var limit = data.Length - 4;
            for (var off = 0; off <= limit; off += 4)
            {
                var val = (uint)(data[off] | (data[off + 1] << 8) | (data[off + 2] << 16) | (data[off + 3] << 24));
                if (targets.Contains(val))
                    result[baseAddr + (ulong)off] = val;
            }
        }
        return result;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        if (needle.Length == 0 || start < 0)
            return -1;
        var last = haystack.Length - needle.Length;
        for (var i = start; i <= last; i++)
        {
            var j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
                j++;
            if (j == needle.Length)
                return i;
        }
        return -1;
    }
}
