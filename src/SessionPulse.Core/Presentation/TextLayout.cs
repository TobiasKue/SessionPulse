using System.Globalization;

namespace SessionPulse.Core.Presentation;

public static class TextLayout
{
    public const string Ellipsis = "…";

    /// <summary>
    /// Word-wraps <paramref name="text"/> into at most <paramref name="maxLines"/> lines of at most
    /// <paramref name="maxChars"/> characters (text elements). Words longer than a line are broken;
    /// if the text doesn't fit, the last line ends with an ellipsis.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, int maxChars, int maxLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);

        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;
            while (rest.Length > 0)
            {
                if (line.Length == 0)
                {
                    if (Length(rest) <= maxChars)
                    {
                        line = rest;
                        rest = string.Empty;
                    }
                    else
                    {
                        lines.Add(Take(rest, maxChars));
                        rest = Skip(rest, maxChars);
                    }
                }
                else if (Length(line) + 1 + Length(rest) <= maxChars)
                {
                    line += " " + rest;
                    rest = string.Empty;
                }
                else
                {
                    lines.Add(line);
                    line = string.Empty;
                }
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        if (lines.Count <= maxLines)
        {
            return lines;
        }

        var kept = lines.Take(maxLines).ToList();
        var last = kept[^1];
        kept[^1] = (Length(last) < maxChars ? last : Take(last, maxChars - 1)).TrimEnd() + Ellipsis;
        return kept;
    }

    /// <summary>
    /// True if <paramref name="text"/> fits into <paramref name="maxLines"/> lines of <paramref name="maxChars"/>
    /// without breaking a word and without an ellipsis.
    /// </summary>
    public static bool FitsWithoutBreaking(string text, int maxChars, int maxLines)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.All(w => Length(w) <= maxChars) && Wrap(text, maxChars, int.MaxValue).Count <= maxLines;
    }

    private static int Length(string s) => new StringInfo(s).LengthInTextElements;

    private static string Take(string s, int count) => new StringInfo(s).SubstringByTextElements(0, Math.Min(count, Length(s)));

    private static string Skip(string s, int count) =>
        Length(s) <= count ? string.Empty : new StringInfo(s).SubstringByTextElements(count);
}
