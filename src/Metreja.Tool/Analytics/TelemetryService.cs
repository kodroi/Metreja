using System.Reflection;
using Metreja.Tool;
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
            {
                DebugLog.Write("telemetry", "skipped: no API key found in assembly metadata");
                return;
            }

            DebugLog.Write("telemetry", "initializing PostHog");

            var options = new PostHogOptions
            {
                ProjectApiKey = apiKey,
                HostUrl = new Uri("https://eu.i.posthog.com"),
                FlushAt = 1,
                FlushInterval = TimeSpan.FromSeconds(1),
            };

            DebugLog.Write("telemetry", $"host: {options.HostUrl}");

            s_client = new PostHogClient(options);

            if (DebugLog.IsEnabled)
                DebugLog.Write("telemetry", $"initialized (distinctId={s_distinctId.Value[..8]}...)");
        }
        catch (Exception ex)
        {
            DebugLog.Write("telemetry", $"initialize failed: {ex.Message}");
        }
    }

    public static void TrackCommand(string commandName, string[] arguments, int exitCode)
    {
        if (s_client is null)
        {
            DebugLog.Write("telemetry", "track skipped: client not initialized");
            return;
        }

        try
        {
            var properties = new Dictionary<string, object>
            {
                ["argument_count"] = arguments.Length,
                ["exit_code"] = exitCode,
                ["os"] = GetOsName(),
                ["cli_version"] = GitVersionInformation.MajorMinorPatch,
            };

            DebugLog.Write("telemetry", $"capture {commandName}: exit_code={exitCode}");

            var queued = s_client.Capture(s_distinctId.Value, commandName, properties);
            DebugLog.Write("telemetry", $"capture queued: {queued}");
        }
        catch (Exception ex)
        {
            DebugLog.Write("telemetry", $"capture failed: {ex.Message}");
        }
    }

    public static async Task FlushAndDisposeAsync()
    {
        if (s_client is null)
            return;

        try
        {
            DebugLog.Write("telemetry", "flushing...");
            await s_client.FlushAsync();
            DebugLog.Write("telemetry", "flushed, disposing...");
            var disposeTask = s_client.DisposeAsync().AsTask();
            var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2)));
            if (completed == disposeTask)
            {
                await disposeTask;
                DebugLog.Write("telemetry", "disposed");
            }
            else
            {
                DebugLog.Write("telemetry", "dispose timed out after 2s");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("telemetry", $"dispose failed: {ex.Message}");
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
