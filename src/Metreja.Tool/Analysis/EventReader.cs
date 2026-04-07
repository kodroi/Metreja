using System.Text.Json;
using Metreja.Tool;

namespace Metreja.Tool.Analysis;

internal static class EventReader
{
    public static async IAsyncEnumerable<(string EventType, JsonElement Root)> StreamEventsAsync(string path)
    {
        var skippedLines = 0;

        await foreach (var line in File.ReadLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                skippedLines++;
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("event", out var eventProp) ||
                    eventProp.ValueKind != JsonValueKind.String)
                {
                    skippedLines++;
                    continue;
                }

                var eventType = eventProp.GetString();
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    skippedLines++;
                    continue;
                }

                yield return (eventType, doc.RootElement.Clone());
            }
        }

        if (skippedLines > 0)
        {
            DebugLog.Write("reader", $"Skipped {skippedLines} malformed or non-event line(s) in {Path.GetFileName(path)}");
        }
    }

    public static (string Ns, string Cls, string M) ExtractMethodInfo(JsonElement root)
    {
        var ns = root.TryGetProperty("ns", out var n) ? n.GetString() ?? "" : "";
        var cls = root.TryGetProperty("cls", out var c) ? c.GetString() ?? "" : "";
        var m = root.TryGetProperty("m", out var mp) ? mp.GetString() ?? "" : "";
        return (ns, cls, m);
    }

    public static string BuildMethodKey(string ns, string cls, string m)
    {
        return string.IsNullOrEmpty(ns) ? $"{cls}.{m}" : $"{ns}.{cls}.{m}";
    }

    public static long GetTid(JsonElement root) => root.TryGetProperty("tid", out var t) ? t.GetInt64() : 0;

    public static long GetTsNs(JsonElement root) => root.TryGetProperty("tsNs", out var t) ? t.GetInt64() : 0;

    public static long GetDeltaNs(JsonElement root) => root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;

    public static bool ValidateFileExists(string path, string label)
    {
        if (File.Exists(path))
            return true;

        Console.Error.WriteLine($"Error: {label} not found: {path}");
        return false;
    }

    public static async Task<Dictionary<string, long>> CollectMethodTimingsAsync(string path)
    {
        var timings = new Dictionary<string, long>();

        await foreach (var (eventType, root) in StreamEventsAsync(path))
        {
            long? timing = eventType switch
            {
                "leave" => root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0,
                "method_stats" => root.TryGetProperty("totalInclusiveNs", out var inc) ? inc.GetInt64() : 0,
                _ => null
            };

            if (timing is not null)
            {
                var (ns, cls, m) = ExtractMethodInfo(root);
                var key = BuildMethodKey(ns, cls, m);
                timings[key] = timings.GetValueOrDefault(key, 0) + timing.Value;
            }
        }

        return timings;
    }

    public static async Task<Dictionary<string, MethodMetrics>> CollectMethodMetricsAsync(string path)
    {
        var metrics = new Dictionary<string, MethodMetrics>();

        await foreach (var (eventType, root) in StreamEventsAsync(path))
        {
            if (eventType is not ("leave" or "method_stats"))
                continue;

            var (ns, cls, m) = ExtractMethodInfo(root);
            var key = BuildMethodKey(ns, cls, m);
            var existing = metrics.GetValueOrDefault(key, MethodMetrics.Empty);

            if (eventType == "method_stats")
            {
                var selfNs = root.TryGetProperty("totalSelfNs", out var s) ? s.GetInt64() : 0;
                var inclNs = root.TryGetProperty("totalInclusiveNs", out var inc) ? inc.GetInt64() : 0;
                var calls = root.TryGetProperty("callCount", out var cc) ? cc.GetInt64() : 0;
                metrics[key] = new MethodMetrics(
                    existing.SelfNs + selfNs,
                    existing.InclusiveNs + inclNs,
                    existing.CallCount + calls,
                    true);
            }
            else // leave
            {
                var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;
                metrics[key] = new MethodMetrics(
                    existing.SelfNs,
                    existing.InclusiveNs + deltaNs,
                    existing.CallCount,
                    existing.HasStats);
            }
        }

        return metrics;
    }
}

internal record MethodMetrics(long SelfNs, long InclusiveNs, long CallCount, bool HasStats)
{
    public static readonly MethodMetrics Empty = new(0, 0, 0, false);
}
