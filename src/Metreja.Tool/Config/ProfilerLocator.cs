namespace Metreja.Tool.Config;

public static class ProfilerLocator
{
    private const string DllName = "Metreja.Profiler.dll";

    public static string? GetDefaultProfilerPath()
    {
        // Adjacent to the CLI assembly (works when installed as dotnet tool and local dev)
        var appBasePath = Path.Combine(AppContext.BaseDirectory, DllName);
        if (File.Exists(appBasePath))
            return appBasePath;

        return null;
    }
}
