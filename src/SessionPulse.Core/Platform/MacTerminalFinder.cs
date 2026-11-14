namespace SessionPulse.Core.Platform;

/// <summary>
/// Finds the app whose window shows a Claude Code CLI session on macOS, given the session's process id
/// (CLAUDE_PID): the first ancestor process that runs from an app bundle (Terminal, iTerm2, VS Code, ...).
/// </summary>
public static class MacTerminalFinder
{
    private const int MaxDepth = 8;
    private const string BundleMarker = ".app/";

    /// <summary>Process names of Claude Code itself: the native build, or the npm package on Node.</summary>
    private static readonly HashSet<string> ClaudeProcesses = new(StringComparer.Ordinal) { "claude", "node" };

    /// <summary>
    /// Returns the path of the app bundle of the terminal, or null. <paramref name="process"/> gives parent id
    /// and executable path (ps -o comm) of a process, or null if it is gone.
    /// </summary>
    public static string? Find(int processId, Func<int, (int Parent, string Command)?> process)
    {
        // A recycled id would point at some unrelated program.
        if (processId <= 0 || process(processId) is not { } self || !ClaudeProcesses.Contains(NameOf(self.Command)))
        {
            return null;
        }

        var seen = new HashSet<int> { processId };
        for (var id = self.Parent; id > 1 && seen.Count <= MaxDepth && seen.Add(id);)
        {
            if (process(id) is not { } info)
            {
                return null;
            }

            if (AppBundleOf(info.Command) is { } bundle)
            {
                return bundle;
            }

            id = info.Parent;
        }

        return null;
    }

    /// <summary>The outermost app bundle in an executable path (helpers of VS Code live inside its bundle), or null.</summary>
    public static string? AppBundleOf(string executablePath)
    {
        var i = executablePath.IndexOf(BundleMarker, StringComparison.Ordinal);
        return i <= 0 || !executablePath.StartsWith('/') ? null : executablePath[..(i + BundleMarker.Length - 1)];
    }

    private static string NameOf(string command)
    {
        var name = command[(command.LastIndexOf('/') + 1)..];
        return name.TrimStart('-').ToLowerInvariant();
    }
}
