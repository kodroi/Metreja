namespace Metreja.Tool.Config;

public static class ProfilerLocator
{
    private const string DllName = "Metreja.Profiler.dll";

    public static string? GetDefaultProfilerPath()
    {
        // Probe 1: Adjacent to the CLI assembly (works when installed as dotnet tool)
        var appBasePath = Path.Combine(AppContext.BaseDirectory, DllName);
        if (File.Exists(appBasePath))
            return appBasePath;

        // Probe 2: Legacy dev layout — bin/Release relative to CWD
        var devPath = Path.Combine("bin", "Release", DllName);
        if (File.Exists(devPath))
            return devPath;

        return null;
    }
}
