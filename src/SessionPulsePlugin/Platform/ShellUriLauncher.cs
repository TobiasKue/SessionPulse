namespace Loupedeck.SessionPulsePlugin;

using System.Diagnostics;
using SessionPulse.Core.Platform;

/// <summary>Opens a URI with the handler registered in the OS (works on Windows and macOS).</summary>
internal sealed class ShellUriLauncher : IUriLauncher
{
    public bool Open(string uri)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"cannot open {uri.Split('?')[0]}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>Fallback for platforms without a window activator yet.</summary>
internal sealed class NoWindowActivator : IWindowActivator
{
    public bool BringClaudeToFront() => false;

    public bool BringTerminalToFront(int processId, string? title, bool wsl) => false;
}
