using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class MemoryAnalyzer
{
    public static async Task AnalyzeAsync(string filePath, int top, string[] filters)
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return;

        var (gcEvents, allocations) = await AggregateAsync(filePath, filters);

        PrintGcSummary(gcEvents);
        Console.WriteLine();
        PrintAllocationTable(allocations, top);
    }

    private static async Task<(GcSummary Gc, Dictionary<string, long> Allocations)> AggregateAsync(
        string filePath, string[] filters)
    {
        var gc = new GcSummary();
        var allocations = new Dictionary<string, long>();
        var hasFilters = filters.Length > 0;

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(filePath))
        {
            switch (eventType)
            {
                case "gc_start":
                {
                    if (root.TryGetProperty("gen0", out var g0) && g0.GetBoolean()) gc.Gen0Count++;
                    if (root.TryGetProperty("gen1", out var g1) && g1.GetBoolean()) gc.Gen1Count++;
                    if (root.TryGetProperty("gen2", out var g2) && g2.GetBoolean()) gc.Gen2Count++;
                    gc.TotalCount++;
                    break;
                }
                case "gc_end":
                {
                    var durationNs = root.TryGetProperty("durationNs", out var d) ? d.GetInt64() : 0;
                    gc.TotalPauseNs += durationNs;
                    if (durationNs > gc.MaxPauseNs) gc.MaxPauseNs = durationNs;
                    break;
                }
                case "alloc_by_class":
                {
                    var className = root.TryGetProperty("className", out var cn) ? cn.GetString() ?? "Unknown" : "Unknown";
                    var count = root.TryGetProperty("count", out var c) ? c.GetInt64() : 0;

                    // Include allocating method if present
                    if (root.TryGetProperty("allocM", out var am) && am.ValueKind == JsonValueKind.String)
                    {
                        var allocNs = root.TryGetProperty("allocNs", out var ans) ? ans.GetString() ?? "" : "";
                        var allocCls = root.TryGetProperty("allocCls", out var acs) ? acs.GetString() ?? "" : "";
                        var allocM = am.GetString() ?? "";
                        var allocKey = AnalyzerHelpers.BuildMethodKey(allocNs, allocCls, allocM);
                        if (!string.IsNullOrEmpty(allocKey) && allocKey != ".")
                            className = $"{className} <- {allocKey}";
                    }

                    if (hasFilters && !AnalyzerHelpers.MatchesAnyFilter(filters, className))
                        break;

                    if (!allocations.TryGetValue(className, out var existing))
                        existing = 0;
                    allocations[className] = existing + count;
                    break;
                }
            }
        }

        return (gc, allocations);
    }

    private static void PrintGcSummary(GcSummary gc)
    {
        Console.WriteLine("GC Summary");
        Console.WriteLine(new string('-', 50));

        if (gc.TotalCount == 0)
        {
            Console.WriteLine("  No GC events recorded.");
            return;
        }

        var avgPauseNs = gc.TotalCount > 0 ? gc.TotalPauseNs / gc.TotalCount : 0;

        Console.WriteLine($"  {"Generation",-15} {"Count",8}");
        Console.WriteLine($"  {"Gen 0",-15} {gc.Gen0Count,8}");
        Console.WriteLine($"  {"Gen 1",-15} {gc.Gen1Count,8}");
        Console.WriteLine($"  {"Gen 2",-15} {gc.Gen2Count,8}");
        Console.WriteLine($"  {"Total",-15} {gc.TotalCount,8}");
        Console.WriteLine();
        Console.WriteLine($"  {"Total pause:",-15} {AnalyzerHelpers.FormatNs(gc.TotalPauseNs)}");
        Console.WriteLine($"  {"Avg pause:",-15} {AnalyzerHelpers.FormatNs(avgPauseNs)}");
        Console.WriteLine($"  {"Max pause:",-15} {AnalyzerHelpers.FormatNs(gc.MaxPauseNs)}");
    }

    private static void PrintAllocationTable(Dictionary<string, long> allocations, int top)
    {
        Console.WriteLine("Allocations by Class");
        Console.WriteLine(new string('-', 70));

        if (allocations.Count == 0)
        {
            Console.WriteLine("  No allocation events recorded.");
            return;
        }

        var sorted = allocations
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .ToList();

        Console.WriteLine($"  {"#",-5} {"Class",-50} {"Count",10}");
        Console.WriteLine($"  {new string('-', 65)}");

        for (var i = 0; i < sorted.Count; i++)
        {
            var (className, count) = sorted[i];
            Console.WriteLine($"  {i + 1,-5} {AnalyzerHelpers.Truncate(className, 50),-50} {count,10}");
        }

        Console.WriteLine($"  {new string('-', 65)}");
        Console.WriteLine($"  Showing top {sorted.Count} of {allocations.Count} types");
    }

    private sealed class GcSummary
    {
        public int TotalCount { get; set; }
        public int Gen0Count { get; set; }
        public int Gen1Count { get; set; }
        public int Gen2Count { get; set; }
        public long TotalPauseNs { get; set; }
        public long MaxPauseNs { get; set; }
    }
}
