using SessionPulse.Core.Platform;

namespace SessionPulse.Core.Tests;

public class MacTerminalFinderTests
{
    private static Func<int, (int Parent, string Command)?> Tree(params (int Id, int Parent, string Command)[] processes) =>
        id => processes.Where(p => p.Id == id).Select(p => ((int, string)?)(p.Parent, p.Command)).FirstOrDefault();

    [Fact]
    public void TerminalAppIsFoundAboveShellAndLogin()
    {
        // As measured on macOS 27: claude -> -zsh -> login -> Terminal.
        var tree = Tree(
            (1076, 1, "/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal"),
            (45355, 1076, "login"),
            (45356, 45355, "-zsh"),
            (45425, 45356, "claude"));

        Assert.Equal("/System/Applications/Utilities/Terminal.app", MacTerminalFinder.Find(45425, tree));
    }

    [Fact]
    public void HelperProcessInsideAnAppBundleGivesTheOuterApp()
    {
        var tree = Tree(
            (5, 1, "/Applications/Visual Studio Code.app/Contents/Frameworks/Code Helper (Plugin).app/Contents/MacOS/Code Helper (Plugin)"),
            (6, 5, "/bin/zsh"),
            (7, 6, "node"));

        Assert.Equal("/Applications/Visual Studio Code.app", MacTerminalFinder.Find(7, tree));
    }

    [Fact]
    public void RecycledOrUnknownProcessGivesNothing()
    {
        var tree = Tree((5, 1, "/Applications/iTerm.app/Contents/MacOS/iTerm2"), (6, 5, "vim"), (8, 1, "claude"));

        Assert.Null(MacTerminalFinder.Find(6, tree));
        Assert.Null(MacTerminalFinder.Find(0, tree));
        Assert.Null(MacTerminalFinder.Find(99, tree));
        Assert.Null(MacTerminalFinder.Find(8, tree));
    }

    [Theory]
    [InlineData("/Applications/iTerm.app/Contents/MacOS/iTerm2", "/Applications/iTerm.app")]
    [InlineData("/usr/bin/login", null)]
    [InlineData("iTerm.app/Contents/MacOS/iTerm2", null)]
    public void AppBundleOfAnExecutablePath(string path, string? expected) =>
        Assert.Equal(expected, MacTerminalFinder.AppBundleOf(path));
}
