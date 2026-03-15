namespace Metreja.Tool.Analysis;

public static class DiffAnalyzer
{
    public static async Task AnalyzeAsync(string basePath, string comparePath)
    {
        if (!AnalyzerHelpers.ValidateFileExists(basePath, "Base file"))
            return;
        if (!AnalyzerHelpers.ValidateFileExists(comparePath, "Compare file"))
            return;

        var baseTimings = await AnalyzerHelpers.CollectMethodTimingsAsync(basePath);
        var compareTimings = await AnalyzerHelpers.CollectMethodTimingsAsync(comparePath);

        Console.WriteLine("Method Timing Diff (base -> compare):");
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"Method",-50} {"Base",12} {"Compare",12} {"Delta",12} {"Change",10}");
        Console.WriteLine(new string('-', 100));

        var allMethods = baseTimings.Keys.Union(compareTimings.Keys)
            .OrderByDescending(k => Math.Abs(compareTimings.GetValueOrDefault(k, 0) - baseTimings.GetValueOrDefault(k, 0)));
        foreach (var method in allMethods)
        {
            var baseNs = baseTimings.GetValueOrDefault(method, 0);
            var compareNs = compareTimings.GetValueOrDefault(method, 0);
            var delta = compareNs - baseNs;
            var deltaPrefix = delta >= 0 ? "+" : "";
            var changeStr = baseNs > 0
                ? $"{(double)delta / baseNs:P0}"
                : compareNs > 0 ? "new" : "";
            Console.WriteLine(
                $"{AnalyzerHelpers.Truncate(method, 50),-50} {AnalyzerHelpers.FormatNs(baseNs),12} {AnalyzerHelpers.FormatNs(compareNs),12} {deltaPrefix + AnalyzerHelpers.FormatNs(Math.Abs(delta)),12} {changeStr,10}");
        }

        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"Methods in base: {baseTimings.Count}, in compare: {compareTimings.Count}");
    }
}
