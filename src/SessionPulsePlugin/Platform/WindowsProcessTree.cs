namespace Loupedeck.SessionPulsePlugin;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SessionPulse.Core.Platform;

/// <summary>
/// Process information for <see cref="TerminalWindowFinder"/>. Parent and console host come from
/// NtQueryInformationProcess (ProcessBasicInformation, ProcessConsoleHostProcess); both only need
/// PROCESS_QUERY_LIMITED_INFORMATION and work for processes of the same user.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsProcessTree : IProcessTree
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessBasicInformation = 0;
    private const int ProcessConsoleHostProcess = 49;

    public string? NameOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.ToLowerInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public int ParentOf(int processId) =>
        Query(processId, h =>
        {
            var info = default(BasicInformation);
            return NtQueryInformationProcess(h, ProcessBasicInformation, ref info, Marshal.SizeOf<BasicInformation>(), out _) == 0
                ? (int)info.InheritedFromUniqueProcessId
                : 0;
        });

    public int ConsoleHostOf(int processId) =>
        Query(processId, h =>
            NtQueryInformationProcess(h, ProcessConsoleHostProcess, out var host, IntPtr.Size, out _) == 0
                ? (int)(host.ToInt64() & ~3L)   // the low bits are flags
                : 0);

    public bool HasWindow(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var window = process.MainWindowHandle;
            return window != IntPtr.Zero && IsWindowVisible(window);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public IReadOnlyList<int> ProcessesNamed(string name)
    {
        var processes = Process.GetProcessesByName(name);
        var ids = processes.Select(p => p.Id).ToList();
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return ids;
    }

    private static int Query(int processId, Func<IntPtr, int> query)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            return query(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref BasicInformation processInformation, int processInformationLength, out int returnLength);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, out IntPtr processInformation, int processInformationLength, out int returnLength);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);
}
