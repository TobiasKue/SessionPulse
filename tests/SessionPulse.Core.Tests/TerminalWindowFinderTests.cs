using SessionPulse.Core.Platform;

namespace SessionPulse.Core.Tests;

public class TerminalWindowFinderTests
{
    [Fact]
    public void ClassicConsoleUsesTheConsoleHostWindow()
    {
        var tree = new FakeTree()
            .Add(10, "explorer", 0, window: true)
            .Add(20, "cmd", 10)
            .Add(21, "conhost", 20, window: true)
            .Add(30, "claude", 20, consoleHost: 21);

        Assert.Equal(21, TerminalWindowFinder.Find(30, tree));
    }

    [Fact]
    public void ConsoleHandedOverToWindowsTerminalUsesItsWindow()
    {
        // As measured on Windows 11: cmd started from Explorer, the console handed over to Windows Terminal.
        // The stub conhost is a child of cmd and has no window; nothing links the session to the terminal.
        var tree = new FakeTree()
            .Add(10660, "explorer", 0, window: true)
            .Add(12904, "cmd", 10660)
            .Add(2584, "conhost", 12904)
            .Add(22616, "claude", 12904, consoleHost: 2584)
            .Add(19904, "windowsterminal", 1, window: true);

        Assert.Equal(19904, TerminalWindowFinder.Find(22616, tree));
    }

    [Fact]
    public void TerminalThatStartedTheShellIsFoundAsAncestor()
    {
        var tree = new FakeTree()
            .Add(5, "code", 1, window: true)
            .Add(6, "openconsole", 5)
            .Add(7, "pwsh", 5)
            .Add(8, "node", 7, consoleHost: 6)
            .Add(9, "windowsterminal", 1, window: true);

        Assert.Equal(5, TerminalWindowFinder.Find(8, tree));
    }

    [Fact]
    public void ExplorerIsNeverTheAnswer()
    {
        var tree = new FakeTree()
            .Add(10, "explorer", 0, window: true)
            .Add(20, "cmd", 10)
            .Add(30, "claude", 20);

        Assert.Null(TerminalWindowFinder.Find(30, tree));
    }

    [Fact]
    public void GoneOrRecycledProcessIdsAreIgnored()
    {
        var tree = new FakeTree()
            .Add(30, "notepad", 1, window: true)
            .Add(9, "windowsterminal", 1, window: true);

        Assert.Null(TerminalWindowFinder.Find(30, tree));
        Assert.Null(TerminalWindowFinder.Find(31, tree));
        Assert.Null(TerminalWindowFinder.Find(0, tree));
    }

    [Fact]
    public void ParentLoopsEnd()
    {
        var tree = new FakeTree()
            .Add(1, "cmd", 2)
            .Add(2, "cmd", 1)
            .Add(3, "claude", 1);

        Assert.Null(TerminalWindowFinder.Find(3, tree));
    }

    private sealed class FakeTree : IProcessTree
    {
        private readonly Dictionary<int, (string Name, int Parent, int ConsoleHost, bool Window)> processes = [];

        public FakeTree Add(int id, string name, int parent, int consoleHost = 0, bool window = false)
        {
            this.processes[id] = (name, parent, consoleHost, window);
            return this;
        }

        public string? NameOf(int processId) => this.processes.TryGetValue(processId, out var p) ? p.Name : null;

        public int ParentOf(int processId) => this.processes.TryGetValue(processId, out var p) ? p.Parent : 0;

        public int ConsoleHostOf(int processId) => this.processes.TryGetValue(processId, out var p) ? p.ConsoleHost : 0;

        public bool HasWindow(int processId) => this.processes.TryGetValue(processId, out var p) && p.Window;

        public IReadOnlyList<int> ProcessesNamed(string name) => [.. this.processes.Where(kv => kv.Value.Name == name).Select(kv => kv.Key)];
    }
}
