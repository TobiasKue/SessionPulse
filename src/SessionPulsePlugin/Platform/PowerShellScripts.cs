namespace Loupedeck.SessionPulsePlugin;

using System.Diagnostics;
using System.Runtime.Versioning;
using SessionPulse.Core.Platform;

/// <summary>
/// Runs the UI Automation helper scripts shipped in bin/Scripts. Only the first output line counts and
/// is awaited with a timeout: started processes may keep stdout open (docs/LESSONS.md). If a group
/// policy blocks PowerShell scripts, the error text from stderr is returned so that it ends up in the log.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PowerShellScripts
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly string scriptsDirectory;

    public PowerShellScripts(string scriptsDirectory) => this.scriptsDirectory = scriptsDirectory;

    /// <summary>Returns the script's first output line, or "ERR ..." if it failed or timed out.</summary>
    public async Task<string> RunAsync(string scriptName, params string[] arguments)
    {
        var script = Path.Combine(this.scriptsDirectory, scriptName);
        if (!File.Exists(script))
        {
            return "ERR script not found: " + script;
        }

        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(arguments))
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("no process");
        }
        catch (Exception ex)
        {
            return "ERR cannot start PowerShell: " + ex.Message;
        }

        using (process)
        {
            var firstLine = process.StandardOutput.ReadLineAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (await Task.WhenAny(firstLine, Task.Delay(DefaultTimeout)).ConfigureAwait(false) != firstLine)
            {
                TryKill(process);
                return "ERR timeout";
            }

            var line = await firstLine.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }

            // No output: typically blocked by policy. Report stderr (briefly) instead.
            if (await Task.WhenAny(errors, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false) == errors)
            {
                var error = (await errors.ConfigureAwait(false)).Trim();
                return "ERR " + (error.Length > 300 ? error[..300] : error);
            }

            return "ERR no output";
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }
}

/// <summary>Scrolls a session to the top with scroll-to-top.ps1, in the background.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsScrollToTop : IScrollToTop
{
    private readonly PowerShellScripts scripts;

    public WindowsScrollToTop(PowerShellScripts scripts) => this.scripts = scripts;

    public void ScrollToTop(string localSessionId) =>
        _ = Task.Run(async () =>
        {
            var result = await this.scripts.RunAsync("scroll-to-top.ps1", "-SessionId", localSessionId).ConfigureAwait(false);
            if (result.StartsWith("OK", StringComparison.Ordinal))
            {
                PluginLog.Verbose("scroll to top: " + result);
            }
            else
            {
                PluginLog.Warning("scroll to top failed: " + result);
            }
        });
}

/// <summary>Fallback where scrolling is not implemented (macOS).</summary>
internal sealed class NoScrollToTop : IScrollToTop
{
    public void ScrollToTop(string localSessionId)
    {
    }
}
