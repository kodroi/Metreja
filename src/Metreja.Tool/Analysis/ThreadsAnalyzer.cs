using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class ThreadsAnalyzer
{
    public static async Task<int> AnalyzeAsync(string filePath, string sortBy, string format = "text", TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(filePath, "File"))
            return 1;

        var threads = await AggregateAsync(filePath);

        if (threads.Count == 0)
        {
            if (format == "json")
            {
                output.WriteLine(JsonSerializer.Serialize(
                    new { Threads = Array.Empty<object>(), TotalThreads = 0 },
                    JsonOutputOptions.Default));
            }
            else
            {
                output.WriteLine("No thread activity found.");
            }

            return 0;
        }

        var globalMinTsNs = threads.Values.Min(t => t.FirstTsNs);

        List<KeyValuePair<long, ThreadStats>> sorted = sortBy switch
        {
            "time" => [.. threads.OrderByDescending(kv => kv.Value.RootTimeNs)],
            _ => [.. threads.OrderByDescending(kv => kv.Value.CallCount)]
        };

        if (format == "json")
        {
            var jsonResult = new
            {
                Threads = sorted.Select(kv =>
                {
                    var firstRelative = kv.Value.FirstTsNs - globalMinTsNs;
                    var lastRelative = kv.Value.LastTsNs - globalMinTsNs;
                    var activeDuration = kv.Value.LastTsNs - kv.Value.FirstTsNs;

                    return new
                    {
                        Tid = kv.Key,
                        kv.Value.CallCount,
                        kv.Value.RootTimeNs,
                        FirstEventNs = firstRelative,
                        LastEventNs = lastRelative,
                        ActiveDurationNs = activeDuration
                    };
                }).ToList(),
                TotalThreads = threads.Count
            };

            output.WriteLine(JsonSerializer.Serialize(jsonResult, JsonOutputOptions.Default));
            return 0;
        }

        output.WriteLine(
            $"{"TID",-15} {"Calls",7} {"Root Time",12} {"First Event",14} {"Last Event",14} {"Active Duration",17}");
        output.WriteLine(new string('-', 82));

        foreach (var (tid, stats) in sorted)
        {
            var firstRelative = stats.FirstTsNs - globalMinTsNs;
            var lastRelative = stats.LastTsNs - globalMinTsNs;
            var activeDuration = stats.LastTsNs - stats.FirstTsNs;

            output.WriteLine(
                $"{tid,-15} {stats.CallCount,7} {FormatUtils.FormatNs(stats.RootTimeNs),12} {FormatUtils.FormatNs(firstRelative),14} {FormatUtils.FormatNs(lastRelative),14} {FormatUtils.FormatNs(activeDuration),17}");
        }

        output.WriteLine(new string('-', 82));
        output.WriteLine($"Total threads: {threads.Count}");

        return 0;
    }

    private static async Task<Dictionary<long, ThreadStats>> AggregateAsync(string filePath)
    {
        var threads = new Dictionary<long, ThreadStats>();

        await foreach (var (eventType, root) in EventReader.StreamEventsAsync(filePath))
        {
            if (!root.TryGetProperty("tid", out var tidProp))
                continue;

            var tid = tidProp.GetInt64();
            var tsNs = root.TryGetProperty("tsNs", out var tsProp) ? tsProp.GetInt64() : 0;

            if (!threads.TryGetValue(tid, out var stats))
            {
                stats = new ThreadStats();
                threads[tid] = stats;
            }

            if (tsNs < stats.FirstTsNs) stats.FirstTsNs = tsNs;
            if (tsNs > stats.LastTsNs) stats.LastTsNs = tsNs;

            if (eventType == "enter")
            {
                stats.CallCount++;
            }
            else if (eventType == "leave")
            {
                var depth = root.TryGetProperty("depth", out var depthProp) ? depthProp.GetInt32() : -1;
                if (depth == 0)
                {
                    var deltaNs = root.TryGetProperty("deltaNs", out var deltaProp) ? deltaProp.GetInt64() : 0;
                    stats.RootTimeNs += deltaNs;
                }
            }
        }

        return threads;
    }

    private sealed class ThreadStats
    {
        public long CallCount { get; set; }
        public long RootTimeNs { get; set; }
        public long FirstTsNs { get; set; } = long.MaxValue;
        public long LastTsNs { get; set; } = long.MinValue;
    }
}
