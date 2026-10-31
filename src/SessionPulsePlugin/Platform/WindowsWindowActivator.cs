namespace Loupedeck.SessionPulsePlugin;

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SessionPulse.Core.Platform;

/// <summary>
/// Brings the Claude Desktop window, or the terminal of a CLI session, to the front. Windows doesn't let a
/// background process take the focus; attaching to the input of the window in front lifts that lock without
/// simulated keys. A simulated Alt key press is only the fallback: when it reaches Claude, it opens Claude's
/// window menu (docs/LESSONS.md).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsWindowActivator : IWindowActivator
{
    private const string ProcessName = "claude";
    private const int SwRestore = 9;
    private const byte VkMenu = 0x12;
    private const uint KeyeventfKeyup = 0x0002;

    private readonly Action<string> log;
    private readonly PowerShellScripts? scripts;
    private readonly IProcessTree processes = new WindowsProcessTree();

    /// <param name="scripts">Runs show-terminal.ps1 for the exact window and tab; null = heuristics only.</param>
    public WindowsWindowActivator(Action<string> log, PowerShellScripts? scripts = null)
    {
        this.log = log;
        this.scripts = scripts;
    }

    public bool BringClaudeToFront()
    {
        var window = FindClaudeWindow(out var claudeProcesses);
        return window != IntPtr.Zero && this.Activate(window, claudeProcesses, "Claude");
    }

    /// <summary>
    /// Brings an unambiguous terminal window to the front right away (see <see cref="ShowRightAway"/>), then
    /// lets show-terminal.ps1 find the exact window and tab in the background (a PowerShell start takes a
    /// moment) and brings that window to the front.
    /// </summary>
    public bool BringTerminalToFront(int processId, string? title, bool wsl)
    {
        var shown = false;
        if (processId > 0 && TerminalWindowFinder.Find(processId, this.processes) is { } owner && this.ShowRightAway(owner))
        {
            shown = this.ShowMainWindowOf(owner);
        }

        var wanted = TerminalTabs.Normalize(title ?? string.Empty);
        if (this.scripts is null || (processId <= 0 && wanted.Length == 0 && !wsl))
        {
            if (!shown)
            {
                this.log($"no terminal window found for process {processId}");
            }

            return shown;
        }

        _ = Task.Run(() => this.ShowExactTerminalAsync(processId, wanted, wsl));
        return true;
    }

    /// <summary>
    /// Windows Terminal runs all its windows in one process, so its main window is a guess when there are
    /// several; then only the script's answer counts. Other owners (a console window, VS Code) are exact.
    /// </summary>
    private bool ShowRightAway(int owner) =>
        this.scripts is null || this.processes.NameOf(owner) != "windowsterminal" || CountTerminalWindows() == 1;

    private bool ShowMainWindowOf(int owner)
    {
        try
        {
            using var process = Process.GetProcessById(owner);
            var window = process.MainWindowHandle;
            return window != IntPtr.Zero && this.Activate(window, [(uint)owner], process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;   // exited meanwhile
        }
    }

    private async Task ShowExactTerminalAsync(int processId, string title, bool wsl)
    {
        var arguments = new List<string>();
        if (processId > 0)
        {
            arguments.AddRange(["-ProcessId", processId.ToString(CultureInfo.InvariantCulture)]);
        }

        if (title.Length > 0)
        {
            arguments.AddRange(["-Title", title]);
        }

        if (wsl)
        {
            arguments.Add("-Wsl");
        }

        // "OK <window> tab" or "OK <window> window"
        var result = await this.scripts!.RunAsync("show-terminal.ps1", [.. arguments]).ConfigureAwait(false);
        var parts = result.Split(' ');
        if (parts.Length < 2 || parts[0] != "OK" || !long.TryParse(parts[1], CultureInfo.InvariantCulture, out var handle) || handle == 0)
        {
            this.log("terminal not found: " + result);
            return;
        }

        // No owner: one Windows Terminal process has several windows, and another of them may be in front.
        this.Activate(new IntPtr(handle), [], "terminal");
    }

    /// <summary>Visible top-level Windows Terminal windows.</summary>
    private static int CountTerminalWindows()
    {
        var count = 0;
        for (var window = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "CASCADIA_HOSTING_WINDOW_CLASS", null);
             window != IntPtr.Zero;
             window = FindWindowEx(IntPtr.Zero, window, "CASCADIA_HOSTING_WINDOW_CLASS", null))
        {
            if (IsWindowVisible(window))
            {
                count++;
            }
        }

        return count;
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    /// <summary>Restores and focuses <paramref name="window"/>, unless one of <paramref name="owners"/> is already in front.</summary>
    private bool Activate(IntPtr window, HashSet<uint> owners, string what)
    {
        if (IsIconic(window))
        {
            ShowWindow(window, SwRestore);
        }

        var foreground = GetForegroundWindow();
        _ = GetWindowThreadProcessId(foreground, out var foregroundProcess);
        if (foreground == window || owners.Contains(foregroundProcess))
        {
            // Already in front (main window or one of its popups): nothing to do.
            return true;
        }

        if (SetForegroundWithInputOf(foreground, window))
        {
            return true;
        }

        // Fallback; the Alt press goes to the window in front, which is not the target here.
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyeventfKeyup, UIntPtr.Zero);
        if (!SetForegroundWindow(window))
        {
            this.log($"SetForegroundWindow failed for the {what} window");
            return false;
        }

        return true;
    }

    /// <summary>Shares the input state of the window in front for the call, so the focus may move.</summary>
    private static bool SetForegroundWithInputOf(IntPtr foreground, IntPtr window)
    {
        var own = GetCurrentThreadId();
        var other = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        var attached = other != 0 && other != own && AttachThreadInput(own, other, true);
        try
        {
            BringWindowToTop(window);
            return SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(own, other, false);
            }
        }
    }

    private static IntPtr FindClaudeWindow(out HashSet<uint> processIds)
    {
        // Claude Desktop runs several processes with this name; only the main one owns a window.
        processIds = [];
        var found = IntPtr.Zero;
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    processIds.Add((uint)process.Id);
                    var handle = process.MainWindowHandle;
                    if (found == IntPtr.Zero && handle != IntPtr.Zero && IsWindowVisible(handle))
                    {
                        found = handle;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited meanwhile.
                }
            }
        }

        return found;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

#pragma warning disable SA1300, IDE1006 // Win32 name
    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
#pragma warning restore SA1300, IDE1006
}
