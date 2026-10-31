using System.Text.Json;
using System.Text.RegularExpressions;
using SessionPulse.Core.Desktop;

namespace SessionPulse.Core.Dictation;

/// <summary>
/// Optional dictation feature, configured in &lt;status dir&gt;/dictation.json:
/// {"chatId": "...", "startButtons": "Name|Other name", "stopButtons": "Name|Other name"}.
/// Button names are the accessible names of Claude Desktop's own dictation buttons; several names
/// (e.g. per language) are separated by "|". Without a valid file the feature is off.
/// </summary>
public sealed partial record DictationConfig(string ChatId, string StartButtons, string StopButtons)
{
    public const string FileName = "dictation.json";

    public string ChatLink => DeepLinks.ForChat(this.ChatId);

    public static DictationConfig? Load(string path, Action<string>? log = null)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path), log) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("cannot read " + path + ": " + ex.Message);
            return null;
        }
    }

    public static DictationConfig? Parse(string json, Action<string>? log = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var chatId = Get(root, "chatId");
            var start = Get(root, "startButtons");
            var stop = Get(root, "stopButtons");
            if (chatId is null || !ChatIdPattern().IsMatch(chatId) || string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(stop))
            {
                log?.Invoke("dictation.json needs chatId (letters, digits, '-'), startButtons and stopButtons");
                return null;
            }

            return new DictationConfig(chatId, start.Trim(), stop.Trim());
        }
        catch (JsonException ex)
        {
            log?.Invoke("dictation.json is not valid JSON: " + ex.Message);
            return null;
        }
    }

    private static string? Get(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex ChatIdPattern();
}
