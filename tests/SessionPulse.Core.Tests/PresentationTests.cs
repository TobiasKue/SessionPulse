using System.Globalization;
using SessionPulse.Core.Presentation;
using SessionPulse.Core.Status;

namespace SessionPulse.Core.Tests;

public class PresentationTests
{
    [Theory]
    [InlineData("Fix the failing test", 10, 3, new[] { "Fix the", "failing", "test" })]
    [InlineData("short", 10, 3, new[] { "short" })]
    [InlineData("  many   spaces  here ", 20, 2, new[] { "many spaces here" })]
    [InlineData("Supercalifragilistic", 8, 3, new[] { "Supercal", "ifragili", "stic" })]
    [InlineData("one two three four five six", 9, 2, new[] { "one two", "three…" })]
    [InlineData("aaaaaaaa bbbbbbbb cccc", 8, 2, new[] { "aaaaaaaa", "bbbbbbb…" })]
    [InlineData("Über Ärger für Öl", 6, 3, new[] { "Über", "Ärger", "für Öl" })]
    [InlineData("", 5, 2, new string[0])]
    public void WrapsText(string text, int maxChars, int maxLines, string[] expected) =>
        Assert.Equal(expected, TextLayout.Wrap(text, maxChars, maxLines));

    [Fact]
    public void WrapNeverExceedsLimits()
    {
        var random = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var words = Enumerable.Range(0, random.Next(0, 12)).Select(_ => new string('x', random.Next(1, 15)));
            var maxChars = random.Next(2, 12);
            var maxLines = random.Next(1, 4);

            var lines = TextLayout.Wrap(string.Join(' ', words), maxChars, maxLines);

            Assert.True(lines.Count <= maxLines);
            Assert.All(lines, l => Assert.True(l.Length <= maxChars, $"'{l}' longer than {maxChars}"));
        }
    }

    [Theory]
    [InlineData("SessionPulse", 12, 1, true)]
    [InlineData("SessionPulse", 11, 3, false)]
    [InlineData("a b c d", 3, 2, true)]
    [InlineData("a b c d e", 3, 2, false)]
    public void ChecksFitWithoutBreaking(string text, int maxChars, int maxLines, bool expected) =>
        Assert.Equal(expected, TextLayout.FitsWithoutBreaking(text, maxChars, maxLines));

    [Fact]
    public void DetailTextsAreShort()
    {
        foreach (var detail in Enum.GetValues<StatusDetail>())
        {
            Assert.True(Texts.German.Detail(detail, null).Length <= 10, detail.ToString());
            Assert.True(Texts.English.Detail(detail, null).Length <= 10, detail.ToString());
        }
    }

    [Theory]
    [InlineData(-5, "0s")]
    [InlineData(42, "42s")]
    [InlineData(60, "1m")]
    [InlineData(3599, "59m")]
    [InlineData(3900, "1:05h")]
    [InlineData(86400 * 3 + 5, "3d")]
    public void FormatsDurations(int seconds, string expected) =>
        Assert.Equal(expected, DurationFormat.Short(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void PicksLanguageFromCulture()
    {
        Assert.Equal("Denkt", Texts.ForCulture(new CultureInfo("de-AT")).Detail(StatusDetail.Thinking, null));
        Assert.Equal("Thinking", Texts.ForCulture(new CultureInfo("fr-FR")).Detail(StatusDetail.Thinking, null));
        Assert.Equal("Bash", Texts.English.Detail(StatusDetail.Tool, "Bash"));
    }

    [Fact]
    public void EveryDetailHasATextInBothLanguages()
    {
        foreach (var detail in Enum.GetValues<StatusDetail>())
        {
            Assert.False(string.IsNullOrWhiteSpace(Texts.English.Detail(detail, null)));
            Assert.False(string.IsNullOrWhiteSpace(Texts.German.Detail(detail, null)));
        }
    }
}
