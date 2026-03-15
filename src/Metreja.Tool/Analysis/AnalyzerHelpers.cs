using System.Text.Json;

namespace Metreja.Tool.Analysis;

internal static class AnalyzerHelpers
{
    public static string FormatNs(long ns)
    {
        return ns switch
        {
            < 1_000 => $"{ns}ns",
            < 1_000_000 => $"{ns / 1_000.0:F2}us",
            < 1_000_000_000 => $"{ns / 1_000_000.0:F2}ms",
            _ => $"{ns / 1_000_000_000.0:F2}s"
        };
    }

    public static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat("...", value.AsSpan(value.Length - maxLength + 3));
    }

    public static bool MatchesPattern(string pattern, string ns, string cls, string m)
    {
        var full = string.IsNullOrEmpty(ns) ? $"{cls}.{m}" : $"{ns}.{cls}.{m}";
        var clsMethod = $"{cls}.{m}";

        return string.Equals(m, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clsMethod, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(full, pattern, StringComparison.OrdinalIgnoreCase) ||
               full.Contains(pattern, StringComparison.OrdinalIgnoreCase);
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

    public static bool ValidateFileExists(string path, string label)
    {
        if (File.Exists(path))
            return true;

        Console.Error.WriteLine($"Error: {label} not found: {path}");
        return false;
    }

    public static async IAsyncEnumerable<(string EventType, JsonElement Root)> StreamEventsAsync(string path)
    {
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
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("event", out var eventProp) ||
                    eventProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var eventType = eventProp.GetString();
                if (string.IsNullOrWhiteSpace(eventType))
                    continue;

                yield return (eventType, doc.RootElement.Clone());
            }
        }
    }

    public static bool MatchesAnyFilter(string[] filters, string ns, string cls, string method, string fullKey)
    {
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                continue;
            }

            if (MatchesPattern(filter, ns, cls, method) ||
                fullKey.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesAnyFilter(string[] filters, string className)
    {
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                continue;
            }

            if (className.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

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
}
