namespace Metreja.Tool.Analysis;

internal static class MethodMatcher
{
    public static bool MatchesPattern(string pattern, string ns, string cls, string m)
    {
        var full = string.IsNullOrEmpty(ns) ? $"{cls}.{m}" : $"{ns}.{cls}.{m}";
        var clsMethod = $"{cls}.{m}";

        return string.Equals(m, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clsMethod, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(full, pattern, StringComparison.OrdinalIgnoreCase) ||
               full.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesAnyFilter(string[] filters, string ns, string cls, string method, string fullKey)
    {
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            if (MatchesPattern(filter, ns, cls, method) ||
                fullKey.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool MatchesAnyFilter(string[] filters, string className)
    {
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            if (className.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
