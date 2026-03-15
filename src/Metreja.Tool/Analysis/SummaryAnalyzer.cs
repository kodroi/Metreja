using System.Globalization;
using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class SummaryAnalyzer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> AnalyzeAsync(string filePath, string format = "text")
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return 1;

        var summary = await AggregateAsync(filePath);

        if (format == "json")
        {
            var durationNs = summary.MinTsNs.HasValue && summary.MaxTsNs.HasValue
                ? summary.MaxTsNs.Value - summary.MinTsNs.Value
                : 0L;

            var jsonOutput = new
            {
                sessionId = summary.SessionId,
                scenario = summary.Scenario,
                pid = summary.Pid,
                durationNs,
                threadCount = summary.ThreadIds.Count,
                methodCount = summary.MethodKeys.Count,
                totalEvents = summary.TotalEvents,
                eventBreakdown = summary.EventCounts.OrderByDescending(kv => kv.Value)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                gcCollections = summary.GcCount,
                exceptionCount = summary.ExceptionCount,
            };

            Console.WriteLine(JsonSerializer.Serialize(jsonOutput, s_jsonOptions));
            return 0;
        }

        Console.WriteLine("Trace Summary");
        Console.WriteLine(new string('-', 50));
        Console.WriteLine($"  Session:      {summary.SessionId}");
        Console.WriteLine($"  Scenario:     {summary.Scenario}");
        Console.WriteLine($"  PID:          {summary.Pid}");

        var duration = summary.MinTsNs.HasValue && summary.MaxTsNs.HasValue
            ? AnalyzerHelpers.FormatNs(summary.MaxTsNs.Value - summary.MinTsNs.Value)
            : "N/A";
        Console.WriteLine($"  Duration:     {duration}");
        Console.WriteLine($"  Threads:      {summary.ThreadIds.Count}");
        Console.WriteLine($"  Methods:      {summary.MethodKeys.Count}");
        Console.WriteLine($"  Total events: {summary.TotalEvents}");
        Console.WriteLine();
        Console.WriteLine("  Event breakdown:");

        foreach (var (eventType, count) in summary.EventCounts.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"    {eventType,-18}{count}");
        }

        Console.WriteLine();
        Console.WriteLine($"  GC collections:  {summary.GcCount}");
        Console.WriteLine($"  Exceptions:     {summary.ExceptionCount}");

        return 0;
    }

    private static async Task<TraceSummary> AggregateAsync(string filePath)
    {
        var summary = new TraceSummary();

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(filePath))
        {
            summary.TotalEvents++;

            if (!summary.EventCounts.TryGetValue(eventType, out var count))
                count = 0;
            summary.EventCounts[eventType] = count + 1;

            if (root.TryGetProperty("tsNs", out var tsProp) && tsProp.ValueKind == JsonValueKind.Number)
            {
                var tsNs = tsProp.GetInt64();
                if (tsNs > 0)
                {
                    if (!summary.MinTsNs.HasValue || tsNs < summary.MinTsNs.Value)
                        summary.MinTsNs = tsNs;
                    if (!summary.MaxTsNs.HasValue || tsNs > summary.MaxTsNs.Value)
                        summary.MaxTsNs = tsNs;
                }
            }

            if (root.TryGetProperty("tid", out var tidProp) && tidProp.ValueKind == JsonValueKind.Number)
            {
                summary.ThreadIds.Add(tidProp.GetInt64());
            }

            switch (eventType)
            {
                case "enter":
                case "leave":
                {
                    var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                    var key = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
                    summary.MethodKeys.Add(key);
                    break;
                }
                case "method_stats":
                {
                    var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                    var key = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
                    summary.MethodKeys.Add(key);
                    break;
                }
                case "gc_start":
                    summary.GcCount++;
                    break;
                case "exception":
                    summary.ExceptionCount++;
                    break;
                case "exception_stats":
                {
                    var exCount = root.TryGetProperty("count", out var c) ? c.GetInt64() : 0;
                    summary.ExceptionCount += exCount;
                    break;
                }
                case "session_metadata":
                {
                    if (root.TryGetProperty("sessionId", out var sid))
                        summary.SessionId = sid.GetString() ?? "";
                    if (root.TryGetProperty("scenario", out var sc))
                        summary.Scenario = sc.GetString() ?? "";
                    if (root.TryGetProperty("pid", out var p) && p.ValueKind == JsonValueKind.Number)
                        summary.Pid = p.GetInt64().ToString(CultureInfo.InvariantCulture);
                    break;
                }
            }
        }

        return summary;
    }

    private sealed class TraceSummary
    {
        public long TotalEvents { get; set; }
        public Dictionary<string, long> EventCounts { get; } = [];
        public long? MinTsNs { get; set; }
        public long? MaxTsNs { get; set; }
        public HashSet<long> ThreadIds { get; } = [];
        public HashSet<string> MethodKeys { get; } = [];
        public long GcCount { get; set; }
        public long ExceptionCount { get; set; }
        public string SessionId { get; set; } = "";
        public string Scenario { get; set; } = "";
        public string Pid { get; set; } = "";
    }
}
