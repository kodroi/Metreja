using System.Text.Json;

namespace Metreja.Tool.Analysis;

internal static class AnalyzerHelpers
{
    public static string FormatNs(long ns) => FormatUtils.FormatNs(ns);

    public static string Truncate(string value, int maxLength) => FormatUtils.Truncate(value, maxLength);

    public static bool MatchesPattern(string pattern, string ns, string cls, string m) =>
        MethodMatcher.MatchesPattern(pattern, ns, cls, m);

    public static (string Ns, string Cls, string M) ExtractMethodInfo(JsonElement root) =>
        EventReader.ExtractMethodInfo(root);

    public static string BuildMethodKey(string ns, string cls, string m) =>
        EventReader.BuildMethodKey(ns, cls, m);

    public static bool ValidateFileExists(string path, string label) =>
        EventReader.ValidateFileExists(path, label);

    public static IAsyncEnumerable<(string EventType, JsonElement Root)> StreamEventsAsync(string path) =>
        EventReader.StreamEventsAsync(path);

    public static bool MatchesAnyFilter(string[] filters, string ns, string cls, string method, string fullKey) =>
        MethodMatcher.MatchesAnyFilter(filters, ns, cls, method, fullKey);

    public static bool MatchesAnyFilter(string[] filters, string className) =>
        MethodMatcher.MatchesAnyFilter(filters, className);

    public static Task<Dictionary<string, long>> CollectMethodTimingsAsync(string path) =>
        EventReader.CollectMethodTimingsAsync(path);
}
