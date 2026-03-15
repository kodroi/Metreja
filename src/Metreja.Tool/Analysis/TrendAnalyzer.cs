using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class TrendAnalyzer
{
    public static async Task AnalyzeAsync(string filePath, string methodPattern)
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return;

        var intervals = await CollectIntervalsAsync(filePath, methodPattern);

        if (intervals.Count == 0)
        {
            Console.Error.WriteLine($"No method_stats events found matching '{methodPattern}'");
            return;
        }

        var methodKey = intervals[0].MethodKey;
        var baseTsNs = intervals[0].TsNs;

        Console.WriteLine($"Trend: {methodKey}");
        Console.WriteLine(new string('-', 50));
        Console.WriteLine(
            $"{"#",-5} {"Flush Time",12} {"Calls",7}  {"Self Total",12}  {"Self Avg",12}  {"Incl Total",12}  {"Incl Avg",12}");

        for (var i = 0; i < intervals.Count; i++)
        {
            var iv = intervals[i];
            var relativeTs = iv.TsNs - baseTsNs;
            var selfAvg = iv.CallCount > 0 ? iv.TotalSelfNs / iv.CallCount : 0;
            var inclAvg = iv.CallCount > 0 ? iv.TotalInclusiveNs / iv.CallCount : 0;

            Console.WriteLine(
                $"{i + 1,-5} {AnalyzerHelpers.FormatNs(relativeTs),12} {iv.CallCount,7}  {AnalyzerHelpers.FormatNs(iv.TotalSelfNs),12}  {AnalyzerHelpers.FormatNs(selfAvg),12}  {AnalyzerHelpers.FormatNs(iv.TotalInclusiveNs),12}  {AnalyzerHelpers.FormatNs(inclAvg),12}");
        }

        Console.WriteLine(new string('-', 50));
        Console.WriteLine($"{intervals.Count} intervals found");
    }

    private static async Task<List<TrendInterval>> CollectIntervalsAsync(string filePath, string methodPattern)
    {
        var intervals = new List<TrendInterval>();

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(filePath))
        {
            if (eventType != "method_stats")
                continue;

            var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);

            if (!AnalyzerHelpers.MatchesPattern(methodPattern, ns, cls, m))
                continue;

            var tsNs = root.TryGetProperty("tsNs", out var ts) ? ts.GetInt64() : 0;
            var callCount = root.TryGetProperty("callCount", out var cc) ? cc.GetInt64() : 0;
            var totalSelfNs = root.TryGetProperty("totalSelfNs", out var sn) ? sn.GetInt64() : 0;
            var maxSelfNs = root.TryGetProperty("maxSelfNs", out var smx) ? smx.GetInt64() : 0;
            var totalInclusiveNs = root.TryGetProperty("totalInclusiveNs", out var inc) ? inc.GetInt64() : 0;
            var maxInclusiveNs = root.TryGetProperty("maxInclusiveNs", out var imx) ? imx.GetInt64() : 0;

            intervals.Add(new TrendInterval
            {
                MethodKey = AnalyzerHelpers.BuildMethodKey(ns, cls, m),
                TsNs = tsNs,
                CallCount = callCount,
                TotalSelfNs = totalSelfNs,
                MaxSelfNs = maxSelfNs,
                TotalInclusiveNs = totalInclusiveNs,
                MaxInclusiveNs = maxInclusiveNs
            });
        }

        return intervals;
    }

    private sealed class TrendInterval
    {
        public string MethodKey { get; set; } = "";
        public long TsNs { get; set; }
        public long CallCount { get; set; }
        public long TotalSelfNs { get; set; }
        public long MaxSelfNs { get; set; }
        public long TotalInclusiveNs { get; set; }
        public long MaxInclusiveNs { get; set; }
    }
}
