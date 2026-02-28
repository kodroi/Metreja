using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class CallTreeAnalyzer
{
    public static async Task AnalyzeAsync(string filePath, string methodPattern, long? tidFilter, int occurrence)
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return;

        // Pass 1: Find all occurrences of the method
        var occurrences = await FindOccurrencesAsync(filePath, methodPattern, tidFilter);

        if (occurrences.Count == 0)
        {
            Console.Error.WriteLine($"No invocations found matching '{methodPattern}'");
            return;
        }

        // Sort by deltaNs descending (slowest first)
        occurrences.Sort((a, b) => b.DeltaNs.CompareTo(a.DeltaNs));

        if (occurrence < 1 || occurrence > occurrences.Count)
        {
            Console.Error.WriteLine(
                $"Occurrence {occurrence} out of range (1-{occurrences.Count})");
            return;
        }

        var selected = occurrences[occurrence - 1];

        Console.WriteLine(
            $"Found {occurrences.Count} invocation(s). Showing #{occurrence} (slowest-first).");
        Console.WriteLine(
            $"Call tree for tid {selected.Tid} (total: {AnalyzerHelpers.FormatNs(selected.DeltaNs)})");
        Console.WriteLine(new string('-', 80));

        // Pass 2: Extract and print the subtree
        await PrintSubtreeAsync(filePath, selected);

        Console.WriteLine(new string('-', 80));
    }

    private static async Task<List<Occurrence>> FindOccurrencesAsync(
        string filePath, string methodPattern, long? tidFilter)
    {
        var occurrences = new List<Occurrence>();
        var threadStacks = new Dictionary<long, Stack<int>>();

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
                var tid = root.TryGetProperty("tid", out var t) ? t.GetInt64() : 0;

                if (tidFilter.HasValue && tid != tidFilter.Value)
                    continue;

                if (!threadStacks.TryGetValue(tid, out var stack))
                {
                    stack = new Stack<int>();
                    threadStacks[tid] = stack;
                }

                if (eventType == "enter")
                {
                    stack.Push(stack.Count);
                }
                else if (eventType == "leave")
                {
                    var depth = stack.Count > 0 ? stack.Count - 1 : 0;
                    if (stack.Count > 0) stack.Pop();

                    var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                    var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;
                    var tsNs = root.TryGetProperty("tsNs", out var ts) ? ts.GetInt64() : 0;

                    if (AnalyzerHelpers.MatchesPattern(methodPattern, ns, cls, m))
                    {
                        occurrences.Add(new Occurrence
                        {
                            Tid = tid,
                            Depth = depth,
                            EnterTsNs = tsNs - deltaNs,
                            LeaveTsNs = tsNs,
                            DeltaNs = deltaNs
                        });
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return occurrences;
    }

    private static async Task PrintSubtreeAsync(string filePath, Occurrence target)
    {
        var entries = new List<DisplayEntry>();
        var inSubtree = false;
        // Stack of entries by depth for attaching leave timing
        var depthStack = new Stack<DisplayEntry>();

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
                var tid = root.TryGetProperty("tid", out var t) ? t.GetInt64() : 0;

                if (tid != target.Tid) continue;

                var tsNs = root.TryGetProperty("tsNs", out var ts) ? ts.GetInt64() : 0;

                if (eventType == "enter")
                {
                    var depth = root.TryGetProperty("depth", out var dp) ? dp.GetInt32() : 0;

                    // Detect start of our target subtree
                    if (!inSubtree && depth == target.Depth &&
                        tsNs >= target.EnterTsNs && tsNs <= target.LeaveTsNs)
                    {
                        inSubtree = true;
                    }

                    if (inSubtree && depth >= target.Depth)
                    {
                        var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                        var isAsync = root.TryGetProperty("async", out var asyncProp) &&
                                      asyncProp.ValueKind == JsonValueKind.True;
                        var key = AnalyzerHelpers.BuildMethodKey(ns, cls, m);

                        var entry = new DisplayEntry
                        {
                            Depth = depth - target.Depth,
                            Name = key,
                            IsAsync = isAsync
                        };
                        entries.Add(entry);
                        depthStack.Push(entry);
                    }
                }
                else if (eventType == "leave" && inSubtree)
                {
                    var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;
                    var exType = root.TryGetProperty("ex", out var ex) ? ex.GetString() ?? "" : "";

                    if (depthStack.Count > 0)
                    {
                        var entry = depthStack.Pop();
                        entry.DeltaNs = deltaNs;
                        entry.IsException = !string.IsNullOrEmpty(exType);
                        entry.ExceptionType = exType;
                    }

                    // If we've popped back to the root of the subtree, we're done
                    if (depthStack.Count == 0)
                    {
                        break;
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        // Print collected entries top-down
        foreach (var entry in entries)
        {
            var indent = new string(' ', entry.Depth * 2);
            var asyncTag = entry.IsAsync ? " [async]" : "";
            var timing = entry.DeltaNs >= 0 ? $" ({AnalyzerHelpers.FormatNs(entry.DeltaNs)})" : "";
            Console.WriteLine($"{indent}{entry.Name}{asyncTag}{timing}");

            if (entry.IsException)
            {
                Console.WriteLine($"{indent}  !! {entry.ExceptionType}");
            }
        }
    }

    private sealed class Occurrence
    {
        public long Tid { get; set; }
        public int Depth { get; set; }
        public long EnterTsNs { get; set; }
        public long LeaveTsNs { get; set; }
        public long DeltaNs { get; set; }
    }

    private sealed class DisplayEntry
    {
        public int Depth { get; set; }
        public string Name { get; set; } = "";
        public bool IsAsync { get; set; }
        public long DeltaNs { get; set; } = -1;
        public bool IsException { get; set; }
        public string ExceptionType { get; set; } = "";
    }
}
