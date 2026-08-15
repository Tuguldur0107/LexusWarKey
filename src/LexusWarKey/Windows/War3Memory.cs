using System.Diagnostics;
using System.Runtime.InteropServices;
using LexusWarKey.Core;

namespace LexusWarKey.Windows;

/// <summary>Reads and writes war3.exe's memory to change a skill's hotkey mid-match.
///
/// war3 is a 32-bit process, so every address fits in a uint; this class still uses ulong
/// addresses and the 64-bit MEMORY_BASIC_INFORMATION because our own process is x64. Opening the
/// game needs an ADMIN handle (a non-elevated OpenProcess returns ACCESS_DENIED); the platform
/// launches the game, so it can hold the handle without prompting, and the standalone must run
/// elevated. Every write is a single guarded byte - never a range - matching the probe.</summary>
public sealed class War3Memory : IDisposable
{
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_PRIVATE = 0x20000;
    private const uint MEM_IMAGE = 0x1000000;
    private const uint PAGE_READONLY = 0x02;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_WRITECOPY = 0x08;
    private const ulong UserSpaceEnd = 0x7FFF0000;
    private const long MaxRegion = 64L * 1024 * 1024;

    private IntPtr _handle = IntPtr.Zero;
    private readonly int _pid;

    private War3Memory(IntPtr handle, int pid) { _handle = handle; _pid = pid; }

    public int Pid => _pid;

    public enum OpenError { None, NotRunning, AccessDenied, Failed }

    /// <summary>Opens war3 for reading and writing. Returns null with a reason on failure.</summary>
    public static War3Memory? Open(out OpenError error)
    {
        error = OpenError.None;
        var proc = Process.GetProcessesByName("war3").FirstOrDefault();
        if (proc is null)
        {
            error = OpenError.NotRunning;
            return null;
        }

        const uint access = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION;
        var handle = OpenProcess(access, false, proc.Id);
        if (handle == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error() == 5 ? OpenError.AccessDenied : OpenError.Failed;
            return null;
        }
        return new War3Memory(handle, proc.Id);
    }

    /// <summary>Reads every scannable region into a snapshot. war3 is ~400-500 MB; this holds it
    /// for the length of one overlay action, same as the python probe.</summary>
    public MemorySnapshot Snapshot()
    {
        var regions = new List<(ulong, byte[])>();
        ulong addr = 0;
        while (addr < UserSpaceEnd)
        {
            if (VirtualQueryEx(_handle, (IntPtr)addr, out var mbi, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
                break;

            var wantType = mbi.Type is MEM_PRIVATE or MEM_IMAGE;
            var wantProtect = mbi.Protect is PAGE_READWRITE or PAGE_WRITECOPY or PAGE_READONLY;
            if (mbi.State == MEM_COMMIT && wantType && wantProtect
                && mbi.RegionSize > 0 && (long)mbi.RegionSize < MaxRegion)
            {
                var buffer = new byte[(int)mbi.RegionSize];
                if (ReadProcessMemory(_handle, (IntPtr)mbi.BaseAddress, buffer, buffer.Length, out var got) && got > 0)
                {
                    if (got != buffer.Length)
                        Array.Resize(ref buffer, (int)got);
                    regions.Add(((ulong)mbi.BaseAddress, buffer));
                }
            }

            var next = mbi.BaseAddress + mbi.RegionSize;
            if (next <= addr)   // guard against a zero/looping region
                break;
            addr = next;
        }
        return new MemorySnapshot(regions);
    }

    /// <summary>Base address of a module loaded in war3 (e.g. "Game.dll"), or null. war3 is 32-bit,
    /// so the toolhelp snapshot must include 32-bit modules even though this process is x64.</summary>
    public ulong? GetModuleBase(string moduleName)
    {
        const uint TH32CS_SNAPMODULE = 0x00000008;
        const uint TH32CS_SNAPMODULE32 = 0x00000010;

        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)_pid);
        if (snapshot == new IntPtr(-1) || snapshot == IntPtr.Zero)
            return null;
        try
        {
            var me = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32W>() };
            if (!Module32FirstW(snapshot, ref me))
                return null;
            do
            {
                if (string.Equals(me.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                    return (ulong)me.modBaseAddr.ToInt64();
            } while (Module32NextW(snapshot, ref me));
            return null;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>Reads the single byte at <paramref name="addr"/>, or null if unreadable.</summary>
    public byte? ReadByte(ulong addr)
    {
        var one = new byte[1];
        return ReadProcessMemory(_handle, (IntPtr)addr, one, 1, out var got) && got == 1 ? one[0] : null;
    }

    public enum WriteResult { Ok, NotALetter, Unreadable, WriteFailed }

    /// <summary>Changes one hotkey cell to <paramref name="newLetter"/>, guarded three ways: the
    /// byte there must currently be an uppercase letter (so a wrong address almost always refuses),
    /// exactly one byte is written, and never a range. Mirrors the probe's setkey.</summary>
    public WriteResult SetHotkey(ulong cellAddr, char newLetter, out byte previous)
    {
        previous = 0;
        newLetter = char.ToUpperInvariant(newLetter);
        if (newLetter is < 'A' or > 'Z')
            return WriteResult.NotALetter;

        if (ReadByte(cellAddr) is not { } old)
            return WriteResult.Unreadable;
        previous = old;
        if (old is < (byte)'A' or > (byte)'Z')
            return WriteResult.Unreadable;   // not a live uppercase-letter cell: refuse

        var buf = new[] { (byte)newLetter };
        if (!WriteProcessMemory(_handle, (IntPtr)cellAddr, buf, 1, out var written) || written != 1)
            return WriteResult.WriteFailed;
        return WriteResult.Ok;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint AllocationProtect;
        public uint __alignment1;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint __alignment2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ulong VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32NextW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);
}
