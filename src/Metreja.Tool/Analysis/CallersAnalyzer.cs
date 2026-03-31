using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class CallersAnalyzer
{
    public static async Task<int> AnalyzeAsync(string filePath, string methodPattern, int top, string format = "text", TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(filePath, "File"))
            return 1;

        var (callerStats, totalCalls) = await AggregateAsync(filePath, methodPattern);

        if (callerStats.Count == 0)
        {
            Console.Error.WriteLine($"No calls found matching '{methodPattern}'");
            return 1;
        }

        var sorted = callerStats
            .OrderByDescending(kv => kv.Value.TotalNs)
            .Take(top)
            .ToList();

        if (format == "json")
        {
            var jsonOutput = new
            {
                method = methodPattern,
                totalCalls,
                callers = sorted.Select(kv =>
                {
                    var avg = kv.Value.Count > 0 ? kv.Value.TotalNs / kv.Value.Count : 0;
                    return new
                    {
                        caller = kv.Key,
                        calls = kv.Value.Count,
                        totalNs = kv.Value.TotalNs,
                        avgNs = avg,
                        maxNs = kv.Value.MaxNs
                    };
                }).ToArray()
            };

            output.WriteLine(JsonSerializer.Serialize(jsonOutput, JsonOutputOptions.Default));
        }
        else
        {
            output.WriteLine($"Callers of {methodPattern} ({totalCalls} total calls):");
            output.WriteLine(
                $"  {"Caller",-50} {"Calls",7} {"Total",12} {"Avg",12} {"Max",12}");
            output.WriteLine($"  {new string('-', 93)}");

            foreach (var (caller, s) in sorted)
            {
                var avg = s.Count > 0 ? s.TotalNs / s.Count : 0;
                output.WriteLine(
                    $"  {FormatUtils.Truncate(caller, 50),-50} {s.Count,7} {FormatUtils.FormatNs(s.TotalNs),12} {FormatUtils.FormatNs(avg),12} {FormatUtils.FormatNs(s.MaxNs),12}");
            }

            output.WriteLine($"  {new string('-', 93)}");
        }

        return 0;
    }

    private static async Task<(Dictionary<string, CallerStats> Stats, int TotalCalls)> AggregateAsync(
        string filePath, string methodPattern)
    {
        var callerStats = new Dictionary<string, CallerStats>();
        var threadStacks = new Dictionary<long, Stack<string>>();
        var totalCalls = 0;

        await foreach (var (eventType, root) in EventReader.StreamEventsAsync(filePath))
        {
            var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
            var tid = root.TryGetProperty("tid", out var t) ? t.GetInt64() : 0;
            var key = EventReader.BuildMethodKey(ns, cls, m);

            if (!threadStacks.TryGetValue(tid, out var stack))
            {
                stack = new Stack<string>();
                threadStacks[tid] = stack;
            }

            if (eventType == "enter")
            {
                stack.Push(key);
            }
            else if (eventType == "leave")
            {
                if (stack.Count > 0) stack.Pop();

                if (MethodMatcher.MatchesPattern(methodPattern, ns, cls, m))
                {
                    totalCalls++;
                    var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;

                    var callerKey = stack.Count > 0 ? stack.Peek() : "<root>";

                    if (!callerStats.TryGetValue(callerKey, out var cs))
                    {
                        cs = new CallerStats();
                        callerStats[callerKey] = cs;
                    }

                    cs.Count++;
                    cs.TotalNs += deltaNs;
                    if (deltaNs > cs.MaxNs) cs.MaxNs = deltaNs;
                }
            }
        }

        return (callerStats, totalCalls);
    }

    private sealed class CallerStats
    {
        public int Count { get; set; }
        public long TotalNs { get; set; }
        public long MaxNs { get; set; }
    }
}
