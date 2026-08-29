namespace VideoCall.Server;


public static class Logger
{
    private static readonly object ConsoleLock = new();

    public static void Info(string message) => Write("INFO", message, ConsoleColor.Gray);

    public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);

    public static void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

    private static void Write(string level, string message, ConsoleColor color)
    {
        lock (ConsoleLock)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"[{level}] {DateTime.Now:HH:mm:ss} {message}");
            Console.ForegroundColor = previous;
        }
    }
}
