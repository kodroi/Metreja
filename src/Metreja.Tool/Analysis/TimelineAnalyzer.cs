using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class TimelineAnalyzer
{
    public static async Task<int> AnalyzeAsync(string filePath, long? tidFilter, string? eventTypeFilter, string? methodFilter, int top, string format = "text")
    {
        if (!AnalyzerHelpers.ValidateFileExists(filePath, "File"))
            return 1;

        long? baselineNs = null;
        var remaining = top;
        var jsonEvents = format == "json" ? new List<object>() : null;

        if (format != "json")
        {
            Console.WriteLine(
                $"{"Timestamp",-14}  {"Event",-15}  {"TID",-10}  {"Details"}");
            Console.WriteLine(new string('-', 80));
        }

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(filePath))
        {
            if (remaining <= 0)
                break;

            // Apply event type filter (exact match)
            if (eventTypeFilter is not null &&
                !string.Equals(eventType, eventTypeFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Apply tid filter
            var hasTid = root.TryGetProperty("tid", out var tidProp);
            var tid = hasTid ? tidProp.GetInt64() : (long?)null;

            if (tidFilter.HasValue)
            {
                if (!hasTid || tid != tidFilter.Value)
                    continue;
            }

            // Extract method info if present
            var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
            var hasMethodInfo = !string.IsNullOrEmpty(m);

            // Apply method filter
            if (methodFilter is not null)
            {
                if (!hasMethodInfo)
                    continue;

                if (!AnalyzerHelpers.MatchesPattern(methodFilter, ns, cls, m))
                    continue;
            }

            // Get timestamp and compute relative
            long? tsNs = root.TryGetProperty("tsNs", out var tsProp) ? tsProp.GetInt64() : null;
            if (tsNs.HasValue)
            {
                baselineNs ??= tsNs.Value;
            }

            var relativeNs = tsNs.HasValue && baselineNs.HasValue
                ? tsNs.Value - baselineNs.Value
                : (long?)null;

            if (format == "json")
            {
                var methodKey = hasMethodInfo ? AnalyzerHelpers.BuildMethodKey(ns, cls, m) : (string?)null;
                long? deltaNs = eventType == "leave" && root.TryGetProperty("deltaNs", out var dj) ? dj.GetInt64() : null;
                string? exception = eventType == "exception" && root.TryGetProperty("exType", out var exj) ? exj.GetString() : null;

                // Extract GC-specific fields
                object? gc = eventType switch
                {
                    "gc_start" => (object)new
                    {
                        Gen0 = root.TryGetProperty("gen0", out var g0) && g0.ValueKind == JsonValueKind.True,
                        Gen1 = root.TryGetProperty("gen1", out var g1) && g1.ValueKind == JsonValueKind.True,
                        Gen2 = root.TryGetProperty("gen2", out var g2) && g2.ValueKind == JsonValueKind.True,
                        Reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null
                    },
                    "gc_end" => new
                    {
                        DurationNs = root.TryGetProperty("durationNs", out var dur) ? dur.GetInt64() : (long?)null,
                        HeapSizeBytes = root.TryGetProperty("heapSizeBytes", out var hs) ? hs.GetInt64() : (long?)null
                    },
                    "gc_heap_stats" => (object)new
                    {
                        Gen0SizeBytes = root.TryGetProperty("gen0SizeBytes", out var gs0) ? gs0.GetInt64() : 0,
                        Gen1SizeBytes = root.TryGetProperty("gen1SizeBytes", out var gs1) ? gs1.GetInt64() : 0,
                        Gen2SizeBytes = root.TryGetProperty("gen2SizeBytes", out var gs2) ? gs2.GetInt64() : 0,
                        LohSizeBytes = root.TryGetProperty("lohSizeBytes", out var gsl) ? gsl.GetInt64() : 0,
                        PohSizeBytes = root.TryGetProperty("pohSizeBytes", out var gsp) ? gsp.GetInt64() : 0,
                        PinnedObjectCount = root.TryGetProperty("pinnedObjectCount", out var poc) ? poc.GetInt32() : 0
                    },
                    _ => null
                };

                jsonEvents!.Add(new
                {
                    RelativeNs = relativeNs,
                    Event = eventType,
                    Tid = tid,
                    Method = methodKey,
                    DeltaNs = deltaNs,
                    Exception = exception,
                    Gc = gc
                });
            }
            else
            {
                var relativeStr = relativeNs.HasValue
                    ? AnalyzerHelpers.FormatNs(relativeNs.Value)
                    : "-";
                var tidStr = tid.HasValue ? $"tid:{tid.Value}" : "-";

                switch (eventType)
                {
                    case "enter":
                    {
                        var methodKey = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
                        Console.WriteLine(
                            $"{relativeStr,-14}  {eventType,-15}  {tidStr,-10}  {methodKey}");
                        break;
                    }
                    case "leave":
                    {
                        var methodKey = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
                        var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;
                        Console.WriteLine(
                            $"{relativeStr,-14}  {eventType,-15}  {tidStr,-10}  {methodKey} ({AnalyzerHelpers.FormatNs(deltaNs)})");
                        break;
                    }
                    case "exception":
                    {
                        var methodKey = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
                        var exType = root.TryGetProperty("exType", out var ex) ? ex.GetString() ?? "" : "";
                        Console.WriteLine(
                            $"{relativeStr,-14}  {"exception",-15}  {tidStr,-10}  {methodKey} [{exType}]");
                        break;
                    }
                    case "gc_start" or "gc_end":
                    {
                        var genInfo = FormatGcGenInfo(root);
                        Console.WriteLine(
                            $"{relativeStr,-14}  {eventType,-15}  {"-",-10}  {genInfo}");
                        break;
                    }
                    case "gc_heap_stats":
                    {
                        var gen0 = root.TryGetProperty("gen0SizeBytes", out var s0) ? s0.GetInt64() : 0;
                        var gen1 = root.TryGetProperty("gen1SizeBytes", out var s1) ? s1.GetInt64() : 0;
                        var gen2 = root.TryGetProperty("gen2SizeBytes", out var s2) ? s2.GetInt64() : 0;
                        var loh = root.TryGetProperty("lohSizeBytes", out var sl) ? sl.GetInt64() : 0;
                        var poh = root.TryGetProperty("pohSizeBytes", out var sp) ? sp.GetInt64() : 0;
                        var total = gen0 + gen1 + gen2 + loh + poh;
                        Console.WriteLine(
                            $"{relativeStr,-14}  {"gc_heap_stats",-15}  {"-",-10}  heap: {AnalyzerHelpers.FormatBytes(total)}");
                        break;
                    }
                    default:
                    {
                        Console.WriteLine(
                            $"{relativeStr,-14}  {eventType,-15}  {tidStr,-10}");
                        break;
                    }
                }
            }

            remaining--;
        }

        var totalShown = top - remaining;

        if (format == "json")
        {
            var output = new
            {
                Events = jsonEvents!,
                TotalShown = totalShown,
                Limit = top
            };

            Console.WriteLine(JsonSerializer.Serialize(output, JsonOutputOptions.Default));
        }
        else
        {
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"Showing {totalShown} event(s) (limit: {top})");
        }

        return 0;
    }

    private static string FormatGcGenInfo(JsonElement root)
    {
        var parts = new List<string>();

        if (root.TryGetProperty("gen0", out var g0) && g0.ValueKind == JsonValueKind.True)
            parts.Add("gen0");
        if (root.TryGetProperty("gen1", out var g1) && g1.ValueKind == JsonValueKind.True)
            parts.Add("gen1");
        if (root.TryGetProperty("gen2", out var g2) && g2.ValueKind == JsonValueKind.True)
            parts.Add("gen2");

        if (root.TryGetProperty("durationNs", out var d))
            parts.Add($"duration: {AnalyzerHelpers.FormatNs(d.GetInt64())}");

        if (root.TryGetProperty("heapSizeBytes", out var hs))
            parts.Add($"heap: {AnalyzerHelpers.FormatBytes(hs.GetInt64())}");

        return parts.Count > 0 ? string.Join(", ", parts) : "";
    }
}
