using System.IO;

namespace SackSpace;

/// <summary>
/// A small text log in the data folder (logs\sackspace.log, rolled at 1 MB). Only
/// warnings and errors: what is needed to understand a problem report, nothing personal.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _file;

    public static void Init(string dataFolder)
    {
        var dir = Path.Combine(dataFolder, "logs");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "sackspace.log");
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        if (_file is null) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{(ex is null ? "" : " | " + ex)}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_file);
                if (info.Exists && info.Length > 1_000_000) File.Move(_file, _file + ".1", overwrite: true);
                File.AppendAllText(_file, line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
