using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class DiffAnalyzer
{
    public static async Task<int> AnalyzeAsync(string basePath, string comparePath, string format = "text", TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(basePath, "Base file"))
            return 1;
        if (!EventReader.ValidateFileExists(comparePath, "Compare file"))
            return 1;

        var baseTimings = await EventReader.CollectMethodTimingsAsync(basePath);
        var compareTimings = await EventReader.CollectMethodTimingsAsync(comparePath);

        var allMethods = baseTimings.Keys.Union(compareTimings.Keys)
            .OrderByDescending(k => Math.Abs(compareTimings.GetValueOrDefault(k, 0) - baseTimings.GetValueOrDefault(k, 0)));

        if (format == "json")
        {
            var methods = allMethods.Select(method =>
            {
                var baseNs = baseTimings.GetValueOrDefault(method, 0);
                var compareNs = compareTimings.GetValueOrDefault(method, 0);
                var deltaNs = compareNs - baseNs;
                var changePercent = baseNs > 0 ? (double)deltaNs / baseNs * 100 : 0.0;
                return new { method, baseNs, compareNs, deltaNs, changePercent };
            }).ToList();

            var result = new
            {
                methods,
                baseMethodCount = baseTimings.Count,
                compareMethodCount = compareTimings.Count
            };

            output.WriteLine(JsonSerializer.Serialize(result, JsonOutputOptions.Default));
            return 0;
        }

        output.WriteLine("Method Timing Diff (base -> compare):");
        output.WriteLine(new string('-', 100));
        output.WriteLine($"{"Method",-50} {"Base",12} {"Compare",12} {"Delta",12} {"Change",10}");
        output.WriteLine(new string('-', 100));

        foreach (var method in allMethods)
        {
            var baseNs = baseTimings.GetValueOrDefault(method, 0);
            var compareNs = compareTimings.GetValueOrDefault(method, 0);
            var delta = compareNs - baseNs;
            var deltaPrefix = delta >= 0 ? "+" : "";
            var changeStr = baseNs > 0
                ? $"{(double)delta / baseNs:P0}"
                : compareNs > 0 ? "new" : "";
            output.WriteLine(
                $"{FormatUtils.Truncate(method, 50),-50} {FormatUtils.FormatNs(baseNs),12} {FormatUtils.FormatNs(compareNs),12} {deltaPrefix + FormatUtils.FormatNs(Math.Abs(delta)),12} {changeStr,10}");
        }

        output.WriteLine(new string('-', 100));
        output.WriteLine($"Methods in base: {baseTimings.Count}, in compare: {compareTimings.Count}");

        return 0;
    }
}
