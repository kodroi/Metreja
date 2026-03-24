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
