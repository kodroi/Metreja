using System.Runtime.InteropServices;

namespace Metreja.Tool.Config;

public static class ProfilerLocator
{
    private static string DllName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libMetreja.Profiler.dylib" : "Metreja.Profiler.dll";

    public static string? GetDefaultProfilerPath()
    {
        // Adjacent to the CLI assembly (works when installed as dotnet tool and local dev)
        var appBasePath = Path.Combine(AppContext.BaseDirectory, DllName);
        if (File.Exists(appBasePath))
            return appBasePath;

        return null;
    }

    /// <summary>
    /// Returns the absolute path to the profiler library, or null with an error written to stderr if not found.
    /// </summary>
    public static string? ResolveProfilerPath()
    {
        var detectedPath = GetDefaultProfilerPath();
        if (string.IsNullOrEmpty(detectedPath) || !File.Exists(detectedPath))
        {
            Console.Error.WriteLine("Error: Could not find the profiler library. Ensure it is adjacent to the CLI assembly.");
            return null;
        }

        return Path.GetFullPath(detectedPath);
    }
}
