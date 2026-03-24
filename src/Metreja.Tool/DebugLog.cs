using Microsoft.Extensions.Logging;

namespace Metreja.Tool;

internal static class DebugLog
{
    private const string EnvVar = "METREJA_DEBUG";

    public static bool IsEnabled { get; private set; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVar));

    public static void Enable()
    {
        IsEnabled = true;
        Environment.SetEnvironmentVariable(EnvVar, "1");
    }

    public static void Write(string message)
    {
        if (!IsEnabled)
            return;

        Console.Error.WriteLine($"[metreja-debug] {message}");
    }

    public static void Write(string category, string message)
    {
        if (!IsEnabled)
            return;

        Console.Error.WriteLine($"[metreja-debug:{category}] {message}");
    }
}

internal sealed class DebugLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DebugLogger(categoryName);
    public void Dispose() { }

    private sealed class DebugLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => DebugLog.IsEnabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!DebugLog.IsEnabled) return;
            var shortCategory = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
            DebugLog.Write("posthog", $"[{logLevel}] {shortCategory}: {formatter(state, exception)}");
        }
    }
}
