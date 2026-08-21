namespace TuckPane.Services;

public static class AppLogger
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            AppPaths.EnsureCreated();
            string line = $"{DateTimeOffset.Now:O} [{level}] {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogPath, line);
            }
        }
        catch
        {
            // Logging must never take down the widget.
        }
    }
}
