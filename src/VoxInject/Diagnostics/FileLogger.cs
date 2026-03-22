namespace VoxInject.Diagnostics;

/// <summary>Thread-safe append logger to %AppData%\VoxInject\debug.log.</summary>
internal static class FileLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoxInject", "debug.log");

    private static readonly object _lock = new();

    static FileLogger()
    {
        // Start fresh on each run
        try { File.WriteAllText(LogPath, $"=== VoxInject session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"); }
        catch { }
    }

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId:D2}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (_lock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { }
        }
    }
}
