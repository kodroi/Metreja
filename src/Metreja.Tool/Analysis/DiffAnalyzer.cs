using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class DiffAnalyzer
{
    public static async Task AnalyzeAsync(string basePath, string comparePath)
    {
        if (!AnalyzerHelpers.ValidateFileExists(basePath, "Base file"))
            return;
        if (!AnalyzerHelpers.ValidateFileExists(comparePath, "Compare file"))
            return;

        var baseTimings = await CollectMethodTimingsAsync(basePath);
        var compareTimings = await CollectMethodTimingsAsync(comparePath);

        Console.WriteLine("Method Timing Diff (base -> compare):");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"{"Method",-50} {"Base (ns)",12} {"Compare (ns)",12} {"Delta",10}");
        Console.WriteLine(new string('-', 80));

        var allMethods = baseTimings.Keys.Union(compareTimings.Keys).OrderBy(k => k);
        foreach (var method in allMethods)
        {
            var baseNs = baseTimings.GetValueOrDefault(method, 0);
            var compareNs = compareTimings.GetValueOrDefault(method, 0);
            var delta = compareNs - baseNs;
            var deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Console.WriteLine($"{method,-50} {baseNs,12} {compareNs,12} {deltaStr,10}");
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"Methods in base: {baseTimings.Count}, in compare: {compareTimings.Count}");
    }

    private static async Task<Dictionary<string, long>> CollectMethodTimingsAsync(string path)
    {
        var timings = new Dictionary<string, long>();

        await foreach (var line in File.ReadLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("event", out var eventProp) ||
                    eventProp.GetString() != "leave")
                    continue;

                var asm = root.TryGetProperty("asm", out var a) ? a.GetString() ?? "" : "";
                var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
                var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;

                var key = $"{asm}.{ns}.{cls}.{m}";

                if (timings.TryGetValue(key, out var existing))
                    timings[key] = existing + deltaNs;
                else
                    timings[key] = deltaNs;
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return timings;
    }
}
