using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class CallTreeAnalyzer
{
    public static async Task<int> AnalyzeAsync(string filePath, string methodPattern, long? tidFilter, int occurrence, string format = "text", TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(filePath, "File"))
            return 1;

        // Pass 1: Find all occurrences of the method
        var occurrences = await FindOccurrencesAsync(filePath, methodPattern, tidFilter);

        if (occurrences.Count == 0)
        {
            Console.Error.WriteLine($"No invocations found matching '{methodPattern}'");
            return 1;
        }

        // Sort by deltaNs descending (slowest first)
        occurrences.Sort((a, b) => b.DeltaNs.CompareTo(a.DeltaNs));

        if (occurrence < 1 || occurrence > occurrences.Count)
        {
            Console.Error.WriteLine(
                $"Occurrence {occurrence} out of range (1-{occurrences.Count})");
            return 1;
        }

        var selected = occurrences[occurrence - 1];

        // Pass 2: Collect the subtree entries
        var entries = await CollectSubtreeAsync(filePath, selected);

        if (format == "json")
        {
            var jsonOutput = new
            {
                method = methodPattern,
                occurrences = occurrences.Count,
                showing = occurrence,
                tid = selected.Tid,
                totalNs = selected.DeltaNs,
                tree = entries.Select(e => new
                {
                    name = e.Name,
                    depth = e.Depth,
                    deltaNs = e.DeltaNs,
                    isAsync = e.IsAsync,
                    exception = e.IsException ? e.ExceptionType : null
                }).ToArray()
            };

            output.WriteLine(JsonSerializer.Serialize(jsonOutput, JsonOutputOptions.Default));
        }
        else
        {
            output.WriteLine(
                $"Found {occurrences.Count} invocation(s). Showing #{occurrence} (slowest-first).");
            output.WriteLine(
                $"Call tree for tid {selected.Tid} (total: {FormatUtils.FormatNs(selected.DeltaNs)})");
            output.WriteLine(new string('-', 80));

            PrintEntries(entries, output);

            output.WriteLine(new string('-', 80));
        }

        return 0;
    }

    private static async Task<List<Occurrence>> FindOccurrencesAsync(
        string filePath, string methodPattern, long? tidFilter)
    {
        var occurrences = new List<Occurrence>();
        var threadStacks = new Dictionary<long, Stack<int>>();

        await foreach (var (eventType, root) in EventReader.StreamEventsAsync(filePath))
        {
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
                var depth = root.TryGetProperty("depth", out var dp) ? dp.GetInt32() : (stack.Count > 0 ? stack.Count - 1 : 0);
                if (stack.Count > 0)
                {
                    stack.Pop();
                }

                var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
                var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;
                var tsNs = root.TryGetProperty("tsNs", out var ts) ? ts.GetInt64() : 0;

                if (MethodMatcher.MatchesPattern(methodPattern, ns, cls, m))
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

        return occurrences;
    }

    private static async Task<List<DisplayEntry>> CollectSubtreeAsync(string filePath, Occurrence target)
    {
        var entries = new List<DisplayEntry>();
        var inSubtree = false;
        // Stack of entries by depth for attaching leave timing
        var depthStack = new Stack<DisplayEntry>();

        await foreach (var (eventType, root) in EventReader.StreamEventsAsync(filePath))
        {
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
                    var (ns, cls, m) = EventReader.ExtractMethodInfo(root);
                    var isAsync = root.TryGetProperty("async", out var asyncProp) &&
                                  asyncProp.ValueKind == JsonValueKind.True;
                    var key = EventReader.BuildMethodKey(ns, cls, m);

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
            else if (eventType == "exception" && inSubtree)
            {
                var exType = (root.TryGetProperty("exType", out var ex) && ex.ValueKind == JsonValueKind.String)
                    ? ex.GetString() ?? ""
                    : "";

                if (depthStack.Count > 0 && !string.IsNullOrEmpty(exType))
                {
                    var entry = depthStack.Peek();
                    entry.IsException = true;
                    entry.ExceptionType = exType;
                }
            }
            else if (eventType == "leave" && inSubtree)
            {
                var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;
                var depth = root.TryGetProperty("depth", out var dp) ? dp.GetInt32() : 0;
                var relativeDepth = depth - target.Depth;

                if (depthStack.Count > 0 && relativeDepth >= 0)
                {
                    var entry = depthStack.Pop();
                    entry.DeltaNs = deltaNs;
                }

                // If we've popped back to the root of the subtree, we're done
                if (depthStack.Count == 0)
                {
                    break;
                }
            }
        }

        return entries;
    }

    private static void PrintEntries(List<DisplayEntry> entries, TextWriter output)
    {
        foreach (var entry in entries)
        {
            var indent = new string(' ', entry.Depth * 2);
            var asyncTag = entry.IsAsync ? " [async]" : "";
            var timing = entry.DeltaNs >= 0 ? $" ({FormatUtils.FormatNs(entry.DeltaNs)})" : "";
            output.WriteLine($"{indent}{entry.Name}{asyncTag}{timing}");

            if (entry.IsException)
            {
                output.WriteLine($"{indent}  !! {entry.ExceptionType}");
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
