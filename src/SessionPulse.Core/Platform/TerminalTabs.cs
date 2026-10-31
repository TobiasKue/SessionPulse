using System.Text.RegularExpressions;

namespace SessionPulse.Core.Platform;

/// <summary>
/// Finds the terminal tab of a CLI session by its title. Claude Code sets the terminal title to the session
/// title with a status glyph in front ("✳ Fix the login", a spinner frame while working); Windows Terminal
/// shows it as the tab name (docs/LESSONS.md). Scripts/show-terminal.ps1 applies the same rule.
/// </summary>
public static partial class TerminalTabs
{
    /// <summary>
    /// Leading status glyphs: whitespace, symbols (✳, braille spinner frames, ●), asterisks and middle dots.
    /// Duplicated in Scripts/show-terminal.ps1; a test checks that both use this exact pattern. Kept ASCII (the
    /// middle dot as a regex escape): Windows PowerShell 5.1 reads scripts without a BOM as ANSI.
    /// </summary>
    public const string StatusPrefixPattern = @"^[\s\p{So}\p{Sm}\p{Sk}*\u00B7]+";

    /// <summary>Shorter titles only match exactly; as a prefix they would match unrelated tabs.</summary>
    public const int MinPrefixLength = 4;

    /// <summary>The title without Claude Code's status glyph, trimmed.</summary>
    public static string Normalize(string title) => StatusPrefix().Replace(title, string.Empty).Trim();

    /// <summary>
    /// Index of the tab showing <paramref name="sessionTitle"/>: the first exact match (ignoring case), else the
    /// first tab that starts with it (the hook cuts titles to 80 characters). Null if none.
    /// </summary>
    public static int? Find(string? sessionTitle, IReadOnlyList<string> tabNames)
    {
        var wanted = Normalize(sessionTitle ?? string.Empty);
        if (wanted.Length == 0)
        {
            return null;
        }

        var names = tabNames.Select(Normalize).ToList();
        var exact = names.FindIndex(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0)
        {
            return exact;
        }

        var prefix = wanted.Length >= MinPrefixLength ? names.FindIndex(n => n.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)) : -1;
        return prefix >= 0 ? prefix : null;
    }

    [GeneratedRegex(StatusPrefixPattern)]
    private static partial Regex StatusPrefix();
}
