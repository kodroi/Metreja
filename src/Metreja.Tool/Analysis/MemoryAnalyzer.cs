using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class MemoryAnalyzer
{
    public static async Task<int> AnalyzeAsync(string filePath, int top, string[] filters, string format = "text", TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(filePath, "File"))
            return 1;

        var (gcEvents, allocations) = await AggregateAsync(filePath, filters);

        if (format == "json")
        {
            var sorted = allocations
                .OrderByDescending(kv => kv.Value)
                .Take(top)
                .Select(kv => new { className = kv.Key, count = kv.Value })
                .ToList();

            var jsonOutput = new
            {
                gc = new
                {
                    totalCount = gcEvents.TotalCount,
                    gen0Count = gcEvents.Gen0Count,
                    gen1Count = gcEvents.Gen1Count,
                    gen2Count = gcEvents.Gen2Count,
                    totalPauseNs = gcEvents.TotalPauseNs,
                    maxPauseNs = gcEvents.MaxPauseNs,
                    peakHeapSizeBytes = gcEvents.HasHeapSize ? gcEvents.PeakHeapSizeBytes : (long?)null,
                    lastHeapSizeBytes = gcEvents.HasHeapSize ? gcEvents.LastHeapSizeBytes : (long?)null,
                    gen0SizeBytes = gcEvents.HasHeapStats ? gcEvents.LastGen0SizeBytes : (long?)null,
                    gen1SizeBytes = gcEvents.HasHeapStats ? gcEvents.LastGen1SizeBytes : (long?)null,
                    gen2SizeBytes = gcEvents.HasHeapStats ? gcEvents.LastGen2SizeBytes : (long?)null,
                    lohSizeBytes = gcEvents.HasHeapStats ? gcEvents.LastLohSizeBytes : (long?)null,
                    pohSizeBytes = gcEvents.HasHeapStats ? gcEvents.LastPohSizeBytes : (long?)null,
                    totalPromotedBytes = gcEvents.HasHeapStats ? gcEvents.TotalPromotedBytes : (long?)null,
                    finalizationQueueLength = gcEvents.HasHeapStats ? gcEvents.LastFinalizationQueueLength : (long?)null,
                    pinnedObjectCount = gcEvents.HasHeapStats ? (int?)gcEvents.LastPinnedObjectCount : null,
                },
                allocations = sorted,
                totalTypes = allocations.Count,
            };

            output.WriteLine(JsonSerializer.Serialize(jsonOutput, JsonOutputOptions.Default));
            return 0;
        }

        PrintGcSummary(gcEvents, output);
        output.WriteLine();
        PrintAllocationTable(allocations, top, output);

        return 0;
    }

    private static async Task<(GcSummary Gc, Dictionary<string, long> Allocations)> AggregateAsync(
        string filePath, string[] filters)
    {
        var gc = new GcSummary();
        var allocations = new Dictionary<string, long>();
        var hasFilters = filters.Length > 0;

        await foreach (var (eventType, root) in EventReader.StreamEventsAsync(filePath))
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
                    if (root.TryGetProperty("heapSizeBytes", out var hs))
                    {
                        gc.HasHeapSize = true;
                        var heapSize = hs.GetInt64();
                        gc.LastHeapSizeBytes = heapSize;
                        if (heapSize > gc.PeakHeapSizeBytes) gc.PeakHeapSizeBytes = heapSize;
                    }
                    break;
                }
                case "gc_heap_stats":
                {
                    gc.HasHeapStats = true;
                    gc.LastGen0SizeBytes = root.TryGetProperty("gen0SizeBytes", out var g0s) ? g0s.GetInt64() : 0;
                    gc.LastGen1SizeBytes = root.TryGetProperty("gen1SizeBytes", out var g1s) ? g1s.GetInt64() : 0;
                    gc.LastGen2SizeBytes = root.TryGetProperty("gen2SizeBytes", out var g2s) ? g2s.GetInt64() : 0;
                    gc.LastLohSizeBytes = root.TryGetProperty("lohSizeBytes", out var lohs) ? lohs.GetInt64() : 0;
                    gc.LastPohSizeBytes = root.TryGetProperty("pohSizeBytes", out var pohs) ? pohs.GetInt64() : 0;
                    gc.LastGen0PromotedBytes = root.TryGetProperty("gen0PromotedBytes", out var p0) ? p0.GetInt64() : 0;
                    gc.LastGen1PromotedBytes = root.TryGetProperty("gen1PromotedBytes", out var p1) ? p1.GetInt64() : 0;
                    gc.LastGen2PromotedBytes = root.TryGetProperty("gen2PromotedBytes", out var p2) ? p2.GetInt64() : 0;
                    gc.LastLohPromotedBytes = root.TryGetProperty("lohPromotedBytes", out var pl) ? pl.GetInt64() : 0;
                    gc.LastPohPromotedBytes = root.TryGetProperty("pohPromotedBytes", out var pp) ? pp.GetInt64() : 0;
                    gc.TotalPromotedBytes += gc.LastGen0PromotedBytes + gc.LastGen1PromotedBytes
                        + gc.LastGen2PromotedBytes + gc.LastLohPromotedBytes + gc.LastPohPromotedBytes;
                    gc.LastFinalizationQueueLength = root.TryGetProperty("finalizationQueueLength", out var fq) ? fq.GetInt64() : 0;
                    gc.LastPinnedObjectCount = root.TryGetProperty("pinnedObjectCount", out var po) ? po.GetInt32() : 0;
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
                        var allocKey = EventReader.BuildMethodKey(allocNs, allocCls, allocM);
                        if (!string.IsNullOrEmpty(allocKey) && allocKey != ".")
                            className = $"{className} <- {allocKey}";
                    }

                    if (hasFilters && !MethodMatcher.MatchesAnyFilter(filters, className))
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

    private static void PrintGcSummary(GcSummary gc, TextWriter output)
    {
        output.WriteLine("GC Summary");
        output.WriteLine(new string('-', 50));

        if (gc.TotalCount == 0)
        {
            output.WriteLine("  No GC events recorded.");
            return;
        }

        var avgPauseNs = gc.TotalCount > 0 ? gc.TotalPauseNs / gc.TotalCount : 0;

        output.WriteLine($"  {"Generation",-15} {"Count",8}");
        output.WriteLine($"  {"Gen 0",-15} {gc.Gen0Count,8}");
        output.WriteLine($"  {"Gen 1",-15} {gc.Gen1Count,8}");
        output.WriteLine($"  {"Gen 2",-15} {gc.Gen2Count,8}");
        output.WriteLine($"  {"Total",-15} {gc.TotalCount,8}");
        output.WriteLine();
        output.WriteLine($"  {"Total pause:",-15} {FormatUtils.FormatNs(gc.TotalPauseNs)}");
        output.WriteLine($"  {"Avg pause:",-15} {FormatUtils.FormatNs(avgPauseNs)}");
        output.WriteLine($"  {"Max pause:",-15} {FormatUtils.FormatNs(gc.MaxPauseNs)}");

        if (gc.PeakHeapSizeBytes > 0)
        {
            output.WriteLine();
            output.WriteLine($"  {"Peak heap:",-15} {FormatUtils.FormatBytes(gc.PeakHeapSizeBytes)}");
            output.WriteLine($"  {"Last heap:",-15} {FormatUtils.FormatBytes(gc.LastHeapSizeBytes)}");
        }

        if (gc.HasHeapStats)
        {
            output.WriteLine();
            output.WriteLine($"  {"Heap",-15} {"Size",12} {"Promoted",12}");
            output.WriteLine($"  {"Gen 0",-15} {FormatUtils.FormatBytes(gc.LastGen0SizeBytes),12} {FormatUtils.FormatBytes(gc.LastGen0PromotedBytes),12}");
            output.WriteLine($"  {"Gen 1",-15} {FormatUtils.FormatBytes(gc.LastGen1SizeBytes),12} {FormatUtils.FormatBytes(gc.LastGen1PromotedBytes),12}");
            output.WriteLine($"  {"Gen 2",-15} {FormatUtils.FormatBytes(gc.LastGen2SizeBytes),12} {FormatUtils.FormatBytes(gc.LastGen2PromotedBytes),12}");
            output.WriteLine($"  {"LOH",-15} {FormatUtils.FormatBytes(gc.LastLohSizeBytes),12} {FormatUtils.FormatBytes(gc.LastLohPromotedBytes),12}");
            if (gc.LastPohSizeBytes > 0)
                output.WriteLine($"  {"POH",-15} {FormatUtils.FormatBytes(gc.LastPohSizeBytes),12} {FormatUtils.FormatBytes(gc.LastPohPromotedBytes),12}");
            output.WriteLine();
            output.WriteLine($"  {"Total promoted:",-15} {FormatUtils.FormatBytes(gc.TotalPromotedBytes)}");
            output.WriteLine($"  {"Finalization:",-15} {gc.LastFinalizationQueueLength}");
            output.WriteLine($"  {"Pinned objects:",-15} {gc.LastPinnedObjectCount}");
        }
    }

    private static void PrintAllocationTable(Dictionary<string, long> allocations, int top, TextWriter output)
    {
        output.WriteLine("Allocations by Class");
        output.WriteLine(new string('-', 70));

        if (allocations.Count == 0)
        {
            output.WriteLine("  No allocation events recorded.");
            return;
        }

        var sorted = allocations
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .ToList();

        output.WriteLine($"  {"#",-5} {"Class",-50} {"Count",10}");
        output.WriteLine($"  {new string('-', 65)}");

        for (var i = 0; i < sorted.Count; i++)
        {
            var (className, count) = sorted[i];
            output.WriteLine($"  {i + 1,-5} {FormatUtils.Truncate(className, 50),-50} {count,10}");
        }

        output.WriteLine($"  {new string('-', 65)}");
        output.WriteLine($"  Showing top {sorted.Count} of {allocations.Count} types");
    }

    private sealed class GcSummary
    {
        public int TotalCount { get; set; }
        public int Gen0Count { get; set; }
        public int Gen1Count { get; set; }
        public int Gen2Count { get; set; }
        public long TotalPauseNs { get; set; }
        public long MaxPauseNs { get; set; }
        public bool HasHeapSize { get; set; }
        public long PeakHeapSizeBytes { get; set; }
        public long LastHeapSizeBytes { get; set; }
        public long LastGen0SizeBytes { get; set; }
        public long LastGen1SizeBytes { get; set; }
        public long LastGen2SizeBytes { get; set; }
        public long LastLohSizeBytes { get; set; }
        public long LastPohSizeBytes { get; set; }
        public long LastGen0PromotedBytes { get; set; }
        public long LastGen1PromotedBytes { get; set; }
        public long LastGen2PromotedBytes { get; set; }
        public long LastLohPromotedBytes { get; set; }
        public long LastPohPromotedBytes { get; set; }
        public long TotalPromotedBytes { get; set; }
        public long LastFinalizationQueueLength { get; set; }
        public int LastPinnedObjectCount { get; set; }
        public bool HasHeapStats { get; set; }
    }
}
