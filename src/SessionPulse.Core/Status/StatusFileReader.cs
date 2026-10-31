namespace SessionPulse.Core.Status;

public static class StatusFileReader
{
    /// <summary>
    /// Reads and parses one status file. The file is opened with full sharing so that the hook's
    /// atomic rename (mv over the file) never fails because we hold it open (see docs/LESSONS.md).
    /// </summary>
    public static StatusParseResult Read(string path)
    {
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > StatusFileParser.MaxFileBytes)
            {
                return StatusParseResult.Invalid("too large");
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StatusParseResult.Invalid("read failed: " + ex.Message);
        }

        return StatusFileParser.Parse(bytes);
    }
}
