using System.Reflection;
using PostHog;

namespace Metreja.Tool.Analytics;

internal static class TelemetryService
{
    private const string OptOutEnvVar = "METREJA_TELEMETRY_OPT_OUT";

    private static PostHogClient? s_client;
    private static readonly Lazy<string> s_distinctId = new(GetOrCreateDistinctId);

    public static bool IsEnabled =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OptOutEnvVar));

    public static void Initialize()
    {
        if (!IsEnabled || s_client is not null)
            return;

        try
        {
            var apiKey = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "PostHogApiKey")?.Value;

            if (string.IsNullOrEmpty(apiKey))
                return;

            s_client = new PostHogClient(new PostHogOptions
            {
                ProjectApiKey = apiKey,
                FlushAt = 1,
                FlushInterval = TimeSpan.FromSeconds(1),
            });
        }
        catch
        {
            // Never affect the user's command
        }
    }

    public static void TrackCommand(string commandName, string[] arguments, int exitCode)
    {
        if (s_client is null)
            return;

        try
        {
            s_client.Capture(s_distinctId.Value, "cli_command_executed", new Dictionary<string, object>
            {
                ["command"] = commandName,
                ["argument_count"] = arguments.Length,
                ["exit_code"] = exitCode,
                ["os"] = GetOsName(),
                ["cli_version"] = GitVersionInformation.MajorMinorPatch,
            });
        }
        catch
        {
            // Never affect the user's command
        }
    }

    public static async Task FlushAndDisposeAsync()
    {
        if (s_client is null)
            return;

        try
        {
            var disposeTask = s_client.DisposeAsync().AsTask();
            await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        catch
        {
            // Never affect the user's command
        }
        finally
        {
            s_client = null;
        }
    }

    private static string GetOrCreateDistinctId()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".metreja");
        var idFile = Path.Combine(dir, "anonymous-id");

        if (File.Exists(idFile))
        {
            var existing = File.ReadAllText(idFile).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;
        }

        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid().ToString();
        File.WriteAllText(idFile, id);
        return id;
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "other";
    }
}
