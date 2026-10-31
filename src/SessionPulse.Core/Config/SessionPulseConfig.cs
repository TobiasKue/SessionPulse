using System.Globalization;

namespace SessionPulse.Core.Config;

/// <summary>
/// Settings from &lt;status dir&gt;/sessionpulse.conf, shared by the hook and the plugin.
/// Format: KEY=VALUE per line, # comments, unknown keys and invalid values are ignored.
/// The table of keys lives in docs/configuration.md.
/// </summary>
public sealed record SessionPulseConfig
{
    public static readonly SessionPulseConfig Default = new();

    /// <summary>Hook: derive Cowork labels from the first prompt.</summary>
    public bool Labels { get; init; } = true;

    /// <summary>Hook: maximum label length in characters (1-200).</summary>
    public int LabelLength { get; init; } = 28;

    /// <summary>Plugin: a working session without activity for this long is shown as possibly stuck.</summary>
    public int StaleMinutes { get; init; } = 10;

    /// <summary>Plugin: status files untouched for this long are deleted.</summary>
    public int CleanupHours { get; init; } = 24;

    /// <summary>Plugin: verbose diagnostics in the plugin log.</summary>
    public bool Verbose { get; init; }

    /// <summary>Hook: hold permission requests for a decision on the keypad (opt-in).</summary>
    public bool Approvals { get; init; }

    /// <summary>Hook: seconds to wait for a keypad decision before Claude asks as usual (5-280).</summary>
    public int ApprovalTimeout { get; init; } = 30;

    /// <summary>Plugin: raise haptic events (MX Master 4).</summary>
    public bool Haptics { get; init; } = true;

    /// <summary>Plugin: Claude Desktop data directory, if not the default %LOCALAPPDATA%\Claude-Data.</summary>
    public string? DesktopDataDirectory { get; init; }

    public static SessionPulseConfig Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Default;
        }
    }

    public static SessionPulseConfig Parse(string text)
    {
        var config = Default;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith('#'))
            {
                continue;
            }

            var key = line[..eq];
            var value = line[(eq + 1)..];
            config = key switch
            {
                "LABELS" when TryFlag(value, out var b) => config with { Labels = b },
                "LABEL_LEN" when TryInt(value, 1, 200, out var n) => config with { LabelLength = n },
                "STALE_MINUTES" when TryInt(value, 1, 1440, out var n) => config with { StaleMinutes = n },
                "CLEANUP_HOURS" when TryInt(value, 1, 24 * 365, out var n) => config with { CleanupHours = n },
                "VERBOSE" when TryFlag(value, out var b) => config with { Verbose = b },
                "APPROVALS" when TryFlag(value, out var b) => config with { Approvals = b },
                "APPROVAL_TIMEOUT" when TryInt(value, 5, 280, out var n) => config with { ApprovalTimeout = n },
                "HAPTICS" when TryFlag(value, out var b) => config with { Haptics = b },
                "DESKTOP_DATA_DIR" when value.Trim().Length > 0 => config with { DesktopDataDirectory = value.Trim() },
                _ => config,
            };
        }

        return config;
    }

    private static bool TryFlag(string value, out bool result)
    {
        result = value == "1";
        return value is "0" or "1";
    }

    private static bool TryInt(string value, int min, int max, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= min && result <= max;
}
