using System.Globalization;
using System.Text.Json;

namespace SessionPulse.Core.Desktop;

/// <summary>
/// Reads the few fields we need from a Claude Desktop session metadata file, privacy first:
/// at most <see cref="MaxBytes"/> are read, parsing stops at the "systemPrompt" property, and only
/// top-level scalar properties from a fixed allow-list are kept. Fields that come after the limit or
/// after "systemPrompt" are simply missing (e.g. scheduledTaskId sometimes is, docs/LESSONS.md).
/// </summary>
public static class DesktopMetadataReader
{
    public const int MaxBytes = 64 * 1024;

    private const string StopProperty = "systemPrompt";

    public static DesktopSession? Read(string path, DesktopSessionKind kind)
    {
        byte[] buffer;
        int length;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            buffer = new byte[MaxBytes];
            length = stream.ReadAtLeast(buffer, MaxBytes, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Parse(buffer.AsSpan(0, length), Path.GetFileNameWithoutExtension(path), kind);
    }

    public static DesktopSession? Parse(ReadOnlySpan<byte> utf8, string localId, DesktopSessionKind kind)
    {
        string? sessionId = null, cliSessionId = null, title = null, scheduledTaskId = null;
        bool archived = false;
        DateTimeOffset? createdAt = null;

        var reader = new Utf8JsonReader(utf8, isFinalBlock: false, state: default);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (name == StopProperty || !reader.Read())
                {
                    break;
                }

                switch (name)
                {
                    case "sessionId": sessionId = StringValue(ref reader); break;
                    case "cliSessionId": cliSessionId = StringValue(ref reader); break;
                    case "title": title = StringValue(ref reader); break;
                    case "scheduledTaskId": scheduledTaskId = StringValue(ref reader); break;
                    case "isArchived": archived = reader.TokenType == JsonTokenType.True; break;
                    case "createdAt": createdAt = TimeValue(ref reader); break;
                }

                // Skip nested values without reading into them; stop if the value is cut off.
                if ((reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) && !reader.TrySkip())
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Truncated or malformed: keep what we have so far.
        }

        if (sessionId is null && cliSessionId is null)
        {
            return null;
        }

        return new DesktopSession(kind, localId, cliSessionId, string.IsNullOrWhiteSpace(title) ? null : title.Trim(), archived, createdAt, scheduledTaskId);
    }

    private static string? StringValue(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

    private static DateTimeOffset? TimeValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var n) && n > 0)
        {
            // Milliseconds if it looks like it (anything after the year 2286 in seconds).
            return n > 9_999_999_999 ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n);
        }

        if (reader.TokenType == JsonTokenType.String
            && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
        {
            return t;
        }

        return null;
    }
}
