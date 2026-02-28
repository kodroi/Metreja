using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class HotspotsAnalyzer
{
    public static async Task AnalyzeAsync(string filePath, int top, double minMs, string sortBy, string[] filters)
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return;

        var stats = await AggregateAsync(filePath, filters);

        var minNs = (long)(minMs * 1_000_000);
        var filtered = stats
            .Where(kv => kv.Value.SelfTotal >= minNs || kv.Value.InclusiveTotal >= minNs)
            .ToList();

        var sorted = sortBy switch
        {
            "inclusive" => filtered.OrderByDescending(kv => kv.Value.InclusiveTotal).ToList(),
            "calls" => filtered.OrderByDescending(kv => kv.Value.Count).ToList(),
            "allocs" => filtered.OrderByDescending(kv => kv.Value.AllocCount).ToList(),
            _ => filtered.OrderByDescending(kv => kv.Value.SelfTotal).ToList()
        };

        var shown = sorted.Take(top).ToList();

        Console.WriteLine(
            $"{"#",-5} {"Method",-50} {"Calls",7} {"Self Total",12} {"Self Avg",10} {"Incl Total",12} {"Incl Avg",10} {"Allocs",9}");
        Console.WriteLine(new string('-', 116));

        for (var i = 0; i < shown.Count; i++)
        {
            var (method, s) = shown[i];
            var selfAvg = s.Count > 0 ? s.SelfTotal / s.Count : 0;
            var inclAvg = s.Count > 0 ? s.InclusiveTotal / s.Count : 0;

            Console.WriteLine(
                $"{i + 1,-5} {AnalyzerHelpers.Truncate(method, 50),-50} {s.Count,7} {AnalyzerHelpers.FormatNs(s.SelfTotal),12} {AnalyzerHelpers.FormatNs(selfAvg),10} {AnalyzerHelpers.FormatNs(s.InclusiveTotal),12} {AnalyzerHelpers.FormatNs(inclAvg),10} {s.AllocCount,9}");
        }

        Console.WriteLine(new string('-', 116));
        Console.WriteLine(
            $"Showing top {shown.Count} of {stats.Count} methods (min threshold: {minMs:F1}ms, sorted by: {sortBy})");
    }

    private static async Task<Dictionary<string, MethodStats>> AggregateAsync(string filePath, string[] filters)
    {
        var stats = new Dictionary<string, MethodStats>();
        var threadStacks = new Dictionary<long, Stack<StackFrame>>();
        var hasFilters = filters.Length > 0;

        await foreach (var line in File.ReadLinesAsync(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("event", out var eventProp))
                    continue;

                var eventType = eventProp.GetString();
                var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                var tid = root.TryGetProperty("tid", out var t) ? t.GetInt64() : 0;
                var key = AnalyzerHelpers.BuildMethodKey(ns, cls, m);

                if (!threadStacks.TryGetValue(tid, out var stack))
                {
                    stack = new Stack<StackFrame>();
                    threadStacks[tid] = stack;
                }

                if (eventType == "enter")
                {
                    stack.Push(new StackFrame { Key = key });
                }
                else if (eventType == "leave")
                {
                    var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;

                    if (stack.Count > 0)
                    {
                        var frame = stack.Pop();
                        var selfNs = deltaNs - frame.ChildrenNs;

                        if (!hasFilters || MatchesAnyFilter(filters, ns, cls, m, key))
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
                        }

                        if (stack.Count > 0)
                        {
                            stack.Peek().ChildrenNs += deltaNs;
                        }
                    }
                }
                else if (eventType == "alloc_by_class")
                {
                    var allocCount = root.TryGetProperty("count", out var c) ? c.GetInt64() : 0;
                    if (allocCount > 0 && stack.Count > 0)
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
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return stats;
    }

    private static bool MatchesAnyFilter(string[] filters, string ns, string cls, string m, string fullKey)
    {
        foreach (var filter in filters)
        {
            if (fullKey.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m, filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ns, filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class StackFrame
    {
        public string Key = "";
        public long ChildrenNs;
    }

    private sealed class MethodStats
    {
        public int Count;
        public long InclusiveTotal;
        public long InclusiveMax;
        public long SelfTotal;
        public long SelfMax;
        public long AllocCount;
    }
}
