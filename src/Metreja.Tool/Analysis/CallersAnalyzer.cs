using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class CallersAnalyzer
{
    public static async Task AnalyzeAsync(string filePath, string methodPattern, int top)
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return;

        var (callerStats, totalCalls) = await AggregateAsync(filePath, methodPattern);

        if (callerStats.Count == 0)
        {
            Console.Error.WriteLine($"No calls found matching '{methodPattern}'");
            return;
        }

        var sorted = callerStats
            .OrderByDescending(kv => kv.Value.TotalNs)
            .Take(top)
            .ToList();

        Console.WriteLine($"Callers of {methodPattern} ({totalCalls} total calls):");
        Console.WriteLine(
            $"  {"Caller",-50} {"Calls",7} {"Total",12} {"Avg",12} {"Max",12}");
        Console.WriteLine($"  {new string('-', 93)}");

        foreach (var (caller, s) in sorted)
        {
            var avg = s.Count > 0 ? s.TotalNs / s.Count : 0;
            Console.WriteLine(
                $"  {AnalyzerHelpers.Truncate(caller, 50),-50} {s.Count,7} {AnalyzerHelpers.FormatNs(s.TotalNs),12} {AnalyzerHelpers.FormatNs(avg),12} {AnalyzerHelpers.FormatNs(s.MaxNs),12}");
        }

        Console.WriteLine($"  {new string('-', 93)}");
    }

    private static async Task<(Dictionary<string, CallerStats> Stats, int TotalCalls)> AggregateAsync(
        string filePath, string methodPattern)
    {
        var callerStats = new Dictionary<string, CallerStats>();
        var threadStacks = new Dictionary<long, Stack<string>>();
        var totalCalls = 0;

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

                    if (AnalyzerHelpers.MatchesPattern(methodPattern, ns, cls, m))
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
            catch (JsonException)
            {
                // Skip malformed lines
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
