namespace Loupedeck.SessionPulsePlugin;

/// <summary>Static access to the plugin log (Logs\plugin_logs in the Logi Plugin Service data directory).</summary>
internal static class PluginLog
{
    private static PluginLogFile? logFile;

    public static void Init(PluginLogFile pluginLogFile) => logFile = pluginLogFile;

    public static void Verbose(string text) => logFile?.Verbose(text);

    public static void Info(string text) => logFile?.Info(text);

    public static void Warning(string text) => logFile?.Warning(text);

    public static void Error(string text) => logFile?.Error(text);

    public static void Error(Exception ex, string text) => logFile?.Error(ex, text);
}
