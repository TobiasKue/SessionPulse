namespace SessionPulse.Core.Sessions;

internal static class AtomicFile
{
    /// <summary>Writes to a temporary file next to the target and moves it over the target.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}
