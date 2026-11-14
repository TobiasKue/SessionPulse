namespace Loupedeck.SessionPulsePlugin;

using System.Diagnostics;
using SessionPulse.Core.Platform;

/// <summary>
/// macOS: brings Claude Desktop or the terminal app of a CLI session to the front. In Terminal and iTerm2 an
/// AppleScript selects the tab by the session's tty (the first run asks for the Automation permission);
/// other apps (VS Code, Ghostty, ...) are only activated.
/// </summary>
internal sealed class MacWindowActivator : IWindowActivator
{
    private const string ClaudeBundleId = "com.anthropic.claudefordesktop";

    private const string TerminalScript = """
        on run argv
            set wanted to item 1 of argv
            tell application "Terminal"
                repeat with w in windows
                    repeat with t in tabs of w
                        if tty of t is wanted then
                            set selected of t to true
                            set index of w to 1
                            activate
                            return
                        end if
                    end repeat
                end repeat
                activate
            end tell
        end run
        """;

    private const string ITermScript = """
        on run argv
            set wanted to item 1 of argv
            tell application "iTerm2"
                repeat with w in windows
                    repeat with t in tabs of w
                        repeat with s in sessions of t
                            if tty of s is wanted then
                                select w
                                select t
                                select s
                                activate
                                return
                            end if
                        end repeat
                    end repeat
                end repeat
                activate
            end tell
        end run
        """;

    private readonly Action<string> log;

    public MacWindowActivator(Action<string> log) => this.log = log;

    public bool BringClaudeToFront() => this.Start("open", "-b", ClaudeBundleId);

    public bool BringTerminalToFront(int processId, string? title, bool wsl)
    {
        if (processId <= 0 || MacTerminalFinder.Find(processId, ProcessInfo) is not { } bundle)
        {
            this.log($"no terminal app found for process {processId}");
            return false;
        }

        var tty = TtyOf(processId);
        var app = Path.GetFileNameWithoutExtension(bundle);
        return tty is not null && app is ("Terminal" or "iTerm")
            ? this.Start("osascript", "-e", app == "Terminal" ? TerminalScript : ITermScript, tty)
            : this.Start("open", "-a", bundle);
    }

    /// <summary>Parent id and executable path of a process (ps -o ppid=,comm=), or null if it is gone.</summary>
    private static (int Parent, string Command)? ProcessInfo(int processId)
    {
        var line = Ps(processId, "ppid=,comm=");
        if (line is null)
        {
            return null;
        }

        var space = line.IndexOf(' ');
        return space > 0 && int.TryParse(line[..space], out var parent) ? (parent, line[(space + 1)..].Trim()) : null;
    }

    /// <summary>The controlling terminal as AppleScript reports it (/dev/ttys001), or null.</summary>
    private static string? TtyOf(int processId) =>
        Ps(processId, "tty=") is { Length: > 0 } tty && tty != "??" ? "/dev/" + tty : null;

    private static string? Ps(int processId, string format)
    {
        try
        {
            var psi = new ProcessStartInfo("ps") { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(format);
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var ps = Process.Start(psi);
            if (ps is null)
            {
                return null;
            }

            var output = ps.StandardOutput.ReadToEnd().Trim();
            ps.WaitForExit(2000);
            return output.Length == 0 ? null : output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Starts a program without waiting for it (osascript may wait for a permission dialog).</summary>
    private bool Start(string program, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(program) { UseShellExecute = false };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            this.log($"cannot start {program}: {ex.Message}");
            return false;
        }
    }
}
