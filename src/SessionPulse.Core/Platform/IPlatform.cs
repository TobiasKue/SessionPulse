namespace SessionPulse.Core.Platform;

/// <summary>Opens a URI with the registered handler (claude:// deep links).</summary>
public interface IUriLauncher
{
    bool Open(string uri);
}

/// <summary>Brings the Claude Desktop window, or the terminal of a CLI session, to the foreground.</summary>
public interface IWindowActivator
{
    /// <summary>Returns false if no visible Claude window was found.</summary>
    bool BringClaudeToFront();

    /// <summary>
    /// Shows the terminal of a CLI session: brings the window of the Claude Code process
    /// <paramref name="processId"/> to the front (<see cref="TerminalWindowFinder"/>; 0 = unknown, e.g. WSL)
    /// and selects the tab titled <paramref name="title"/> (<see cref="TerminalTabs"/>), which may finish
    /// in the background. With <paramref name="wsl"/> and no such tab, a window showing a WSL console will do.
    /// Returns false if there is nothing to look for.
    /// </summary>
    bool BringTerminalToFront(int processId, string? title, bool wsl);
}

/// <summary>Read-only view of the running processes, for <see cref="TerminalWindowFinder"/>.</summary>
public interface IProcessTree
{
    /// <summary>Process name in lower case without extension, or null if the process is gone.</summary>
    string? NameOf(int processId);

    /// <summary>Parent process id, or 0 if unknown.</summary>
    int ParentOf(int processId);

    /// <summary>Process hosting the console of <paramref name="processId"/> (conhost, OpenConsole), or 0.</summary>
    int ConsoleHostOf(int processId);

    /// <summary>True if the process owns a visible top-level window.</summary>
    bool HasWindow(int processId);

    /// <summary>Running processes with this name (lower case, without extension).</summary>
    IReadOnlyList<int> ProcessesNamed(string name);
}

/// <summary>Scrolls the Claude Desktop conversation of a session to its beginning (scheduled runs).</summary>
public interface IScrollToTop
{
    /// <summary>Starts scrolling in the background; must not block the caller.</summary>
    void ScrollToTop(string localSessionId);
}
