using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Metreja.IntegrationTests.Infrastructure;

public static class ProfilerPrerequisites
{
    public static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Metreja.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not find Metreja.sln by walking up from " + AppContext.BaseDirectory);
    }

    public static string GetProfilerDllPath(string solutionRoot)
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "libMetreja.Profiler.dylib"
            : "Metreja.Profiler.dll";
        return Path.Combine(solutionRoot, "bin", "Release", fileName);
    }

    public static string GetTestAppPath(string solutionRoot)
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Metreja.TestApp.exe"
            : "Metreja.TestApp";
        var testAssembly = typeof(ProfilerPrerequisites).Assembly;
        var framework = new FrameworkName(testAssembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        var targetFramework = $"net{framework.Version.ToString(2)}";
        return Path.Combine(solutionRoot, "test", "Metreja.TestApp", "bin", "Release", targetFramework, fileName);
    }

    public static string? GetSkipReason(string solutionRoot)
    {
        var dll = GetProfilerDllPath(solutionRoot);
        var app = GetTestAppPath(solutionRoot);

        if (!File.Exists(dll) && !File.Exists(app))
            return $"Profiler DLL ({dll}) and TestApp ({app}) not found. Build both in Release first.";
        if (!File.Exists(dll))
            return $"Profiler DLL not found at {dll}. Build the native profiler in Release first.";
        if (!File.Exists(app))
            return $"TestApp not found at {app}. Build the test app in Release first.";

        return null;
    }
}
