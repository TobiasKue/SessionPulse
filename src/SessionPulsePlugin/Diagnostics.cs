namespace Loupedeck.SessionPulsePlugin;

using System.Collections.Concurrent;
using SessionPulse.Core;
using SessionPulse.Core.Presentation;

/// <summary>
/// Small, rate-limited diagnostics: one summary line per minute and at most one log entry per minute
/// and key for drawing errors. Logs counts and states only, never labels or titles (docs/privacy.md).
/// </summary>
internal sealed class Diagnostics
{
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ErrorInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> lastError = new();
    private DateTimeOffset lastSummary = DateTimeOffset.UtcNow;
    private int imageRequests;
    private int presses;

    public void CountImageRequest() => Interlocked.Increment(ref this.imageRequests);

    public void CountPress() => Interlocked.Increment(ref this.presses);

    public void MaybeLogSummary(Dashboard dashboard, bool verbose)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - this.lastSummary < SummaryInterval)
        {
            return;
        }

        this.lastSummary = now;
        var images = Interlocked.Exchange(ref this.imageRequests, 0);
        var keyPresses = Interlocked.Exchange(ref this.presses, 0);
        var keys = dashboard.Keys;
        var used = keys.Count(k => k.State != KeyState.Empty);
        var c = dashboard.Counts;
        var line = $"status: keys {used}/{keys.Count}, working={c.Working} waiting={c.Waiting} done={c.Done}, image requests={images}, presses={keyPresses}";
        if (verbose)
        {
            line += ", keys: " + string.Join(" ", keys.Select(k => $"{k.Number}={k.State}"));
        }

        PluginLog.Info(line);
    }

    public void LogDrawError(int keyNumber, Exception ex) => this.LogDrawError("key " + keyNumber, ex);

    public void LogDrawError(string what, Exception ex) => this.LogRateLimited("draw:" + what, ex, $"drawing {what} failed");

    public void LogTickError(Exception ex) => this.LogRateLimited("tick", ex, "tick failed");

    private void LogRateLimited(string key, Exception ex, string message)
    {
        var now = DateTimeOffset.UtcNow;
        if (this.lastError.TryGetValue(key, out var last) && now - last < ErrorInterval)
        {
            return;
        }

        this.lastError[key] = now;
        PluginLog.Error(ex, message);
    }
}
