using System.Globalization;
using System.Text.Json;

namespace SessionPulse.Core.Usage;

/// <summary>One usage window: percent used (0-100, above 100 when exceeded) and when it resets.</summary>
public readonly record struct UsageWindow(double Percent, long ResetsAt)
{
    /// <summary>After the reset time the window starts again from zero.</summary>
    public UsageWindow AsOf(long now) => this.ResetsAt > 0 && now >= this.ResetsAt ? new UsageWindow(0, 0) : this;
}

/// <summary>Subscription usage as last reported by Claude Code's status line (statusline.sh).</summary>
public sealed record UsageSnapshot(long Updated, UsageWindow? FiveHour, UsageWindow? SevenDay, UsageWindow? Spend)
{
    /// <summary>The fullest window right now, for warnings.</summary>
    public double MaxPercent(long now) =>
        new[] { this.FiveHour, this.SevenDay, this.Spend }.Where(w => w is not null).Select(w => w!.Value.AsOf(now).Percent).DefaultIfEmpty(0).Max();
}

/// <summary>Reads &lt;status dir&gt;/usage.json (written by claude-plugin/statusline/statusline.sh).</summary>
public sealed class UsageStore
{
    public const string FileName = "usage.json";

    private readonly string path;
    private DateTime lastStamp;
    private UsageSnapshot? snapshot;

    public UsageStore(string statusDirectory) => this.path = Path.Combine(statusDirectory, FileName);

    /// <summary>The latest snapshot, or null if the status line helper never ran.</summary>
    public UsageSnapshot? Current
    {
        get
        {
            this.RefreshIfChanged();
            return this.snapshot;
        }
    }

    public static UsageSnapshot? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1)
            {
                return null;
            }

            var updated = root.TryGetProperty("updated", out var u) && u.TryGetInt64(out var n) ? n : 0;
            return new UsageSnapshot(updated, Window(root, "fiveHour"), Window(root, "sevenDay"), Window(root, "spend"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static UsageWindow? Window(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object
            || !w.TryGetProperty("pct", out var pct) || pct.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var resets = w.TryGetProperty("resetsAt", out var r) && r.TryGetInt64(out var n) ? n : 0;
        return new UsageWindow(Math.Max(0, pct.GetDouble()), resets);
    }

    private void RefreshIfChanged()
    {
        try
        {
            var stamp = File.Exists(this.path) ? File.GetLastWriteTimeUtc(this.path) : default;
            if (stamp == this.lastStamp)
            {
                return;
            }

            this.lastStamp = stamp;
            if (stamp != default)
            {
                using var stream = new FileStream(this.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                this.snapshot = Parse(reader.ReadToEnd()) ?? this.snapshot;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>"1:20h", "3d", "12m" until <paramref name="resetsAt"/>, or empty.</summary>
    public static string TimeLeft(long resetsAt, long now) =>
        resetsAt > now ? Presentation.DurationFormat.Short(TimeSpan.FromSeconds(resetsAt - now)) : string.Empty;

    public static string Percent(double value) => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture) + "%";
}
