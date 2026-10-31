namespace SessionPulse.Core.Platform;

/// <summary>
/// Finds the process whose window shows a Claude Code CLI session on Windows, given the session's process id
/// (CLAUDE_PID). Picks the terminal window, never a tab: Windows Terminal has no API to select one.
/// </summary>
public static class TerminalWindowFinder
{
    private const int MaxDepth = 8;

    /// <summary>Process names of Claude Code itself: the native build, or the npm package on Node.</summary>
    private static readonly HashSet<string> ClaudeProcesses = new(StringComparer.Ordinal) { "claude", "node" };

    /// <summary>Started the shell, but its window is the desktop, not the terminal.</summary>
    private static readonly HashSet<string> NotTerminals = new(StringComparer.Ordinal) { "explorer", "svchost", "services", "winlogon" };

    /// <summary>Returns the process id whose main window to bring to the front, or null.</summary>
    public static int? Find(int processId, IProcessTree tree)
    {
        // A recycled id would point at some unrelated program.
        if (processId <= 0 || tree.NameOf(processId) is not { } name || !ClaudeProcesses.Contains(name))
        {
            return null;
        }

        // Classic console: the console host owns the console window.
        var host = tree.ConsoleHostOf(processId);
        if (host > 0 && tree.HasWindow(host))
        {
            return host;
        }

        // Terminals that start the shell themselves (VS Code, a Windows Terminal tab): an ancestor of the
        // session or of its console host owns the window.
        foreach (var start in new[] { processId, host })
        {
            if (FindWindowedAncestor(start, tree) is { } ancestor)
            {
                return ancestor;
            }
        }

        // A console handed over to Windows Terminal (the default terminal app) keeps no process link to it;
        // the stub host is a child of the shell. Take the Windows Terminal window.
        return tree.ProcessesNamed("windowsterminal").Where(tree.HasWindow).Cast<int?>().FirstOrDefault();
    }

    private static int? FindWindowedAncestor(int start, IProcessTree tree)
    {
        var seen = new HashSet<int>();
        for (var id = tree.ParentOf(start); id > 0 && seen.Count < MaxDepth && seen.Add(id); id = tree.ParentOf(id))
        {
            var name = tree.NameOf(id);
            if (name is null || NotTerminals.Contains(name))
            {
                return null;
            }

            if (tree.HasWindow(id))
            {
                return id;
            }
        }

        return null;
    }
}
