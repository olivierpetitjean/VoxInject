namespace VoxInject.Diagnostics;

/// <summary>
/// Thread-safe append logger to %AppData%\VoxInject\debug.log.
/// Resets on each call to Reset().
/// </summary>
public static class FileLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoxInject", "debug.log");

    private static readonly object _lock = new();

    public static void Reset()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        lock (_lock)
            File.WriteAllText(LogPath, $"=== VoxInject session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
    }

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}";
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line);
        }
    }
}
