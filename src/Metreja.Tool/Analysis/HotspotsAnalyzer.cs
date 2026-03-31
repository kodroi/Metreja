using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class HotspotsAnalyzer
{
    public static async Task<int> AnalyzeAsync(string filePath, int top, double minMs, string sortBy, string[] filters, string format = "text", TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(filePath, "File"))
            return 1;

        var stats = await AggregateAsync(filePath, filters);

        var minNs = (long)(minMs * 1_000_000);
        var filtered = stats
            .Where(kv => kv.Value.SelfTotal >= minNs || kv.Value.InclusiveTotal >= minNs)
            .ToList();

        List<KeyValuePair<string, MethodStats>> sorted = sortBy switch
        {
            "inclusive" => [.. filtered.OrderByDescending(kv => kv.Value.InclusiveTotal)],
            "calls" => [.. filtered.OrderByDescending(kv => kv.Value.Count)],
            "allocs" => [.. filtered.OrderByDescending(kv => kv.Value.AllocCount)],
            _ => [.. filtered.OrderByDescending(kv => kv.Value.SelfTotal)]
        };

        var shown = sorted.Take(top).ToList();

        if (format == "json")
        {
            var jsonOutput = new
            {
                Methods = shown.Select(kv =>
                {
                    var s = kv.Value;
                    var selfAvg = s.Count > 0 ? s.SelfTotal / s.Count : 0;
                    var inclAvg = s.Count > 0 ? s.InclusiveTotal / s.Count : 0;
                    return new
                    {
                        Method = kv.Key,
                        Calls = s.Count,
                        SelfTotalNs = s.SelfTotal,
                        SelfAvgNs = selfAvg,
                        InclusiveTotalNs = s.InclusiveTotal,
                        InclusiveAvgNs = inclAvg,
                        s.AllocCount,
                        s.TailcallCount,
                        s.ExceptionCount
                    };
                }).ToList(),
                TotalMethods = stats.Count,
                SortedBy = sortBy,
                MinThresholdMs = minMs
            };

            output.WriteLine(JsonSerializer.Serialize(jsonOutput, JsonOutputOptions.Default));
            return 0;
        }

        output.WriteLine(
            $"{"#",-5} {"Method",-50} {"Calls",7} {"Self Total",12} {"Self Avg",10} {"Incl Total",12} {"Incl Avg",10} {"Allocs",9} {"Tailcalls",10} {"Exceptions",11}");
        output.WriteLine(new string('-', 138));

        for (var i = 0; i < shown.Count; i++)
        {
            var (method, s) = shown[i];
            var selfAvg = s.Count > 0 ? s.SelfTotal / s.Count : 0;
            var inclAvg = s.Count > 0 ? s.InclusiveTotal / s.Count : 0;

            output.WriteLine(
                $"{i + 1,-5} {FormatUtils.Truncate(method, 50),-50} {s.Count,7} {FormatUtils.FormatNs(s.SelfTotal),12} {FormatUtils.FormatNs(selfAvg),10} {FormatUtils.FormatNs(s.InclusiveTotal),12} {FormatUtils.FormatNs(inclAvg),10} {s.AllocCount,9} {s.TailcallCount,10} {s.ExceptionCount,11}");
        }

        output.WriteLine(new string('-', 138));
        output.WriteLine(
            $"Showing top {shown.Count} of {stats.Count} methods (min threshold: {minMs:F1}ms, sorted by: {sortBy})");

        return 0;
    }

    private static async Task<Dictionary<string, MethodStats>> AggregateAsync(string filePath, string[] filters)
    {
        var stats = new Dictionary<string, MethodStats>();
        var threadStacks = new Dictionary<long, Stack<StackFrame>>();
        var hasFilters = filters.Length > 0;

        await foreach (var (eventType, root) in EventReader.StreamEventsAsync(filePath))
        {
            if (eventType == "enter")
            {
                var tid = EventReader.GetTid(root);
                var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
                var key = EventReader.BuildMethodKey(ns, cls, m);

                if (!threadStacks.TryGetValue(tid, out var stack))
                {
                    stack = new Stack<StackFrame>();
                    threadStacks[tid] = stack;
                }

                stack.Push(new StackFrame { Key = key });
            }
            else if (eventType == "leave")
            {
                var tid = EventReader.GetTid(root);
                var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
                var key = EventReader.BuildMethodKey(ns, cls, m);
                var deltaNs = EventReader.GetDeltaNs(root);

                if (threadStacks.TryGetValue(tid, out var stack) && stack.Count > 0)
                {
                    var frame = stack.Pop();
                    var selfNs = deltaNs - frame.ChildrenNs;

                    if (!hasFilters || MethodMatcher.MatchesAnyFilter(filters, ns, cls, m, key))
                    {
                        if (!stats.TryGetValue(key, out var ms))
                        {
                            ms = new MethodStats();
                            stats[key] = ms;
                        }

                        ms.Count++;
                        ms.InclusiveTotal += deltaNs;
                        ms.SelfTotal += selfNs;
                        if (deltaNs > ms.InclusiveMax) ms.InclusiveMax = deltaNs;
                        if (selfNs > ms.SelfMax) ms.SelfMax = selfNs;

                        if (root.TryGetProperty("tailcall", out var tc) && tc.ValueKind == JsonValueKind.True)
                            ms.TailcallCount++;
                    }

                    if (stack.Count > 0)
                    {
                        stack.Peek().ChildrenNs += deltaNs;
                    }
                }
            }
            else if (eventType == "method_stats")
            {
                var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
                var key = EventReader.BuildMethodKey(ns, cls, m);

                if (!hasFilters || MethodMatcher.MatchesAnyFilter(filters, ns, cls, m, key))
                {
                    if (!stats.TryGetValue(key, out var ms))
                    {
                        ms = new MethodStats();
                        stats[key] = ms;
                    }

                    ms.Count += root.TryGetProperty("callCount", out var cc) ? cc.GetInt64() : 0;
                    ms.SelfTotal += root.TryGetProperty("totalSelfNs", out var sn) ? sn.GetInt64() : 0;
                    ms.SelfMax = Math.Max(ms.SelfMax, root.TryGetProperty("maxSelfNs", out var smx) ? smx.GetInt64() : 0);
                    ms.InclusiveTotal += root.TryGetProperty("totalInclusiveNs", out var inc) ? inc.GetInt64() : 0;
                    ms.InclusiveMax = Math.Max(ms.InclusiveMax, root.TryGetProperty("maxInclusiveNs", out var imx) ? imx.GetInt64() : 0);
                }
            }
            else if (eventType == "exception")
            {
                var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
                var key = EventReader.BuildMethodKey(ns, cls, m);

                if (!string.IsNullOrEmpty(key) && key != "." &&
                    (!hasFilters || MethodMatcher.MatchesAnyFilter(filters, ns, cls, m, key)))
                {
                    if (!stats.TryGetValue(key, out var ms))
                    {
                        ms = new MethodStats();
                        stats[key] = ms;
                    }

                    ms.ExceptionCount++;
                }
            }
            else if (eventType == "exception_stats")
            {
                var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
                var key = EventReader.BuildMethodKey(ns, cls, m);
                var exCount = root.TryGetProperty("count", out var ec) ? ec.GetInt64() : 0;

                if (exCount > 0 && !string.IsNullOrEmpty(key) && key != "." &&
                    (!hasFilters || MethodMatcher.MatchesAnyFilter(filters, ns, cls, m, key)))
                {
                    if (!stats.TryGetValue(key, out var ms))
                    {
                        ms = new MethodStats();
                        stats[key] = ms;
                    }

                    ms.ExceptionCount += exCount;
                }
            }
            else if (eventType == "alloc_by_class")
            {
                var tid = EventReader.GetTid(root);
                var allocCount = root.TryGetProperty("count", out var c) ? c.GetInt64() : 0;

                if (allocCount > 0 && threadStacks.TryGetValue(tid, out var stack) && stack.Count > 0)
                {
                    var topKey = stack.Peek().Key;
                    if (!stats.TryGetValue(topKey, out var ms))
                    {
                        ms = new MethodStats();
                        stats[topKey] = ms;
                    }

                    ms.AllocCount += allocCount;
                }
            }
        }

        return stats;
    }

    private sealed class StackFrame
    {
        public string Key { get; set; } = "";
        public long ChildrenNs { get; set; }
    }

    private sealed class MethodStats
    {
        public long Count { get; set; }
        public long InclusiveTotal { get; set; }
        public long InclusiveMax { get; set; }
        public long SelfTotal { get; set; }
        public long SelfMax { get; set; }
        public long AllocCount { get; set; }
        public long TailcallCount { get; set; }
        public long ExceptionCount { get; set; }
    }
}
