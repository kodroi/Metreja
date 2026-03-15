using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class TimelineAnalyzer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

                jsonEvents!.Add(new
                {
                    RelativeNs = relativeNs,
                    Event = eventType,
                    Tid = tid,
                    Method = methodKey,
                    DeltaNs = deltaNs,
                    Exception = exception
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

            Console.WriteLine(JsonSerializer.Serialize(output, s_jsonOptions));
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

        if (root.TryGetProperty("deltaNs", out var d))
            parts.Add($"duration: {AnalyzerHelpers.FormatNs(d.GetInt64())}");

        return parts.Count > 0 ? string.Join(", ", parts) : "";
    }
}
