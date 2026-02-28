using System.Text.Json;

namespace Metreja.Tool.Analysis;

internal static class AnalyzerHelpers
{
    public static string FormatNs(long ns)
    {
        return ns switch
        {
            < 1_000 => $"{ns}ns",
            < 1_000_000 => $"{ns / 1_000.0:F2}us",
            < 1_000_000_000 => $"{ns / 1_000_000.0:F2}ms",
            _ => $"{ns / 1_000_000_000.0:F2}s"
        };
    }

    public static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat("...", value.AsSpan(value.Length - maxLength + 3));
    }

    public static bool MatchesPattern(string pattern, string ns, string cls, string m)
    {
        var full = string.IsNullOrEmpty(ns) ? $"{cls}.{m}" : $"{ns}.{cls}.{m}";
        var clsMethod = $"{cls}.{m}";

        return string.Equals(m, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clsMethod, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(full, pattern, StringComparison.OrdinalIgnoreCase) ||
               full.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public static (string Ns, string Cls, string M) ExtractMethodInfo(JsonElement root)
    {
        var ns = root.TryGetProperty("ns", out var n) ? n.GetString() ?? "" : "";
        var cls = root.TryGetProperty("cls", out var c) ? c.GetString() ?? "" : "";
        var m = root.TryGetProperty("m", out var mp) ? mp.GetString() ?? "" : "";
        return (ns, cls, m);
    }

    public static string BuildMethodKey(string ns, string cls, string m)
    {
        return string.IsNullOrEmpty(ns) ? $"{cls}.{m}" : $"{ns}.{cls}.{m}";
    }

    public static bool ValidateFileExists(string path, string label)
    {
        if (File.Exists(path))
            return true;

        Console.Error.WriteLine($"Error: {label} not found: {path}");
        return false;
    }
}
