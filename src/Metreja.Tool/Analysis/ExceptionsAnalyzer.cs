using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class ExceptionsAnalyzer
{
    public static async Task<int> AnalyzeAsync(string filePath, int top, string[] filters, string format = "text")
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return 1;

        var stats = await AggregateAsync(filePath, filters);

        if (format == "json")
        {
            var sorted = stats
                .OrderByDescending(kv => kv.Value.Count)
                .Take(top)
                .Select(kv => new
                {
                    exceptionType = kv.Key,
                    count = kv.Value.Count,
                    topThrowSites = kv.Value.ThrowSites
                        .OrderByDescending(ts => ts.Value)
                        .Take(3)
                        .Select(ts => new { method = ts.Key, count = ts.Value })
                        .ToList(),
                })
                .ToList();

            var jsonOutput = new
            {
                exceptions = sorted,
                totalTypes = stats.Count,
            };

            Console.WriteLine(JsonSerializer.Serialize(jsonOutput, JsonOutputOptions.Default));
            return 0;
        }

        if (stats.Count == 0)
        {
            Console.WriteLine("No exceptions found");
            return 0;
        }

        var sortedText = stats
            .OrderByDescending(kv => kv.Value.Count)
            .Take(top)
            .ToList();

        Console.WriteLine(
            $"{"Exception Type",-50} {"Count",7}  Top Throw Sites");
        Console.WriteLine(new string('-', 75));

        foreach (var (exType, s) in sortedText)
        {
            var topSites = s.ThrowSites
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => $"{kv.Key} ({kv.Value})")
                .ToList();

            var sitesStr = string.Join(", ", topSites);

            Console.WriteLine(
                $"{AnalyzerHelpers.Truncate(exType, 50),-50} {s.Count,7}  {sitesStr}");
        }

        Console.WriteLine(new string('-', 75));
        Console.WriteLine($"Showing top {sortedText.Count} of {stats.Count} exception types");

        return 0;
    }

    private static async Task<Dictionary<string, ExceptionTypeStats>> AggregateAsync(string filePath, string[] filters)
    {
        var stats = new Dictionary<string, ExceptionTypeStats>();
        var hasFilters = filters.Length > 0;

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(filePath))
        {
            if (eventType == "exception")
            {
                var exType = root.TryGetProperty("exType", out var ex) ? ex.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(exType))
                    continue;

                if (hasFilters && !MatchesAnyExceptionFilter(filters, exType))
                    continue;

                if (!stats.TryGetValue(exType, out var s))
                {
                    s = new ExceptionTypeStats();
                    stats[exType] = s;
                }

                s.Count++;

                var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                var methodKey = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
                if (!string.IsNullOrEmpty(methodKey) && methodKey != ".")
                {
                    s.ThrowSites.TryGetValue(methodKey, out var siteCount);
                    s.ThrowSites[methodKey] = siteCount + 1;
                }
            }
            else if (eventType == "exception_stats")
            {
                var exType = root.TryGetProperty("exType", out var ex) ? ex.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(exType))
                    continue;

                if (hasFilters && !MatchesAnyExceptionFilter(filters, exType))
                    continue;

                var count = root.TryGetProperty("count", out var c) ? c.GetInt64() : 0;
                if (count <= 0)
                    continue;

                if (!stats.TryGetValue(exType, out var s))
                {
                    s = new ExceptionTypeStats();
                    stats[exType] = s;
                }

                s.Count += count;

                var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                var methodKey = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
                if (!string.IsNullOrEmpty(methodKey) && methodKey != ".")
                {
                    s.ThrowSites.TryGetValue(methodKey, out var siteCount);
                    s.ThrowSites[methodKey] = siteCount + count;
                }
            }
        }

        return stats;
    }

    private static bool MatchesAnyExceptionFilter(string[] filters, string exType)
    {
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            if (exType.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed class ExceptionTypeStats
    {
        public long Count { get; set; }
        public Dictionary<string, long> ThrowSites { get; set; } = [];
    }
}
