using SessionPulse.Core.Platform;

namespace SessionPulse.Core.Tests;

public class TerminalTabsTests
{
    [Theory]
    [InlineData("✳ Casual test chat", "Casual test chat")]   // as seen in Windows Terminal
    [InlineData("⠂ Fix the login", "Fix the login")]          // spinner frame while working
    [InlineData("  * Fix the login  ", "Fix the login")]
    [InlineData("Fix the login", "Fix the login")]
    [InlineData("\"Quoted\" title", "\"Quoted\" title")]       // only status glyphs go, not punctuation
    [InlineData("✳", "")]
    public void NormalizeDropsTheStatusGlyph(string tab, string expected) =>
        Assert.Equal(expected, TerminalTabs.Normalize(tab));

    [Fact]
    public void FindsTheExactTabFirst()
    {
        string[] tabs = ["✳ Fix the login page", "PowerShell", "⠐ Fix the login"];

        Assert.Equal(2, TerminalTabs.Find("Fix the login", tabs));
        Assert.Equal(1, TerminalTabs.Find("powershell", tabs));
    }

    [Fact]
    public void FallsBackToAPrefixForCutTitles()
    {
        string[] tabs = ["Ubuntu", "✳ A rather long session title that the hook cut at eighty characters, and more"];

        Assert.Equal(1, TerminalTabs.Find("A rather long session title", tabs));
    }

    [Fact]
    public void ShortOrMissingTitlesDontGuess()
    {
        string[] tabs = ["✳ Fix the login", "Fixtures"];

        Assert.Null(TerminalTabs.Find("Fix", tabs));
        Assert.Null(TerminalTabs.Find(null, tabs));
        Assert.Null(TerminalTabs.Find("✳ ", tabs));
        Assert.Null(TerminalTabs.Find("Deploy", tabs));
    }

    /// <summary>The script must strip the same prefix as the plugin, or it would select another tab.</summary>
    [Fact]
    public void ScriptUsesTheSamePattern()
    {
        var script = File.ReadAllText(Path.Combine(HookContractTests.RepoRoot(), "src", "SessionPulsePlugin", "Scripts", "show-terminal.ps1"));

        Assert.Contains("'" + TerminalTabs.StatusPrefixPattern + "'", script, StringComparison.Ordinal);
        Assert.Contains("$minPrefixLength = " + TerminalTabs.MinPrefixLength, script, StringComparison.Ordinal);
    }
}
