using PostHog;

namespace Metreja.Tool.Analytics;

internal static class TelemetryService
{
    private const string PostHogApiKey = "phc_PLACEHOLDER_KEY";
    private const string OptOutEnvVar = "METREJA_TELEMETRY_OPT_OUT";

    private static PostHogClient? s_client;
    private static readonly Lazy<string> s_distinctId = new(GetOrCreateDistinctId);

    public static bool IsEnabled =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OptOutEnvVar));

    public static void Initialize()
    {
        if (!IsEnabled)
            return;

        s_client = new PostHogClient(new PostHogOptions
        {
            ProjectApiKey = PostHogApiKey,
            FlushAt = 1,
            FlushInterval = TimeSpan.FromSeconds(1),
        });
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
                ["arguments"] = string.Join(" ", arguments),
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await s_client.DisposeAsync();
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

        try
        {
            if (File.Exists(idFile))
                return File.ReadAllText(idFile).Trim();

            Directory.CreateDirectory(dir);
            var id = Guid.NewGuid().ToString();
            File.WriteAllText(idFile, id);
            return id;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "other";
    }
}
