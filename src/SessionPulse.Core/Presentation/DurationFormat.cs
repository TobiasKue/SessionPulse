using System.Globalization;

namespace SessionPulse.Core.Presentation;

public static class DurationFormat
{
    /// <summary>Compact duration for a key header: "42s", "12m", "1:05h", "3d".</summary>
    public static string Short(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalSeconds < 60)
        {
            return ((int)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
        }

        if (duration.TotalMinutes < 60)
        {
            return ((int)duration.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
        }

        if (duration.TotalHours < 24)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}:{duration.Minutes:00}h");
        }

        return ((int)duration.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
    }
}
