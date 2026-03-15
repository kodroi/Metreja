using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class CheckAnalyzer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> AnalyzeAsync(string basePath, string comparePath, double threshold, string format = "text")
    {
        if (!AnalyzerHelpers.ValidateFileExists(basePath, "Base file"))
            return 1;
        if (!AnalyzerHelpers.ValidateFileExists(comparePath, "Compare file"))
            return 1;

        var baseTimings = await AnalyzerHelpers.CollectMethodTimingsAsync(basePath);
        var compareTimings = await AnalyzerHelpers.CollectMethodTimingsAsync(comparePath);

        var regressionCount = 0;
        var allMethods = baseTimings.Keys.Union(compareTimings.Keys)
            .OrderByDescending(k =>
                Math.Abs(compareTimings.GetValueOrDefault(k, 0) - baseTimings.GetValueOrDefault(k, 0)));

        var methodEntries = new List<(string method, string status, long baseNs, long compareNs, double changePercent)>();

        foreach (var method in allMethods)
        {
            var baseNs = baseTimings.GetValueOrDefault(method, 0);
            var compareNs = compareTimings.GetValueOrDefault(method, 0);

            if (baseNs == 0) continue; // new methods aren't regressions

            var changePercent = (double)(compareNs - baseNs) / baseNs * 100;
            var isRegression = changePercent > threshold;

            if (isRegression) regressionCount++;

            var status = isRegression ? "REGRESSION" : "OK";
            methodEntries.Add((method, status, baseNs, compareNs, changePercent));
        }

        if (format == "json")
        {
            var result = new
            {
                result = regressionCount > 0 ? "FAIL" : "PASS",
                regressionCount,
                threshold,
                methods = methodEntries.Select(e => new
                {
                    e.method,
                    e.status,
                    e.baseNs,
                    e.compareNs,
                    e.changePercent
                }).ToList()
            };

            Console.WriteLine(JsonSerializer.Serialize(result, s_jsonOptions));
            return regressionCount > 0 ? 1 : 0;
        }

        Console.WriteLine($"Performance check (threshold: {threshold:F1}%)");
        Console.WriteLine(new string('-', 100));

        foreach (var (method, status, baseNs, compareNs, changePercent) in methodEntries)
        {
            Console.WriteLine(
                $"{status,-12} {AnalyzerHelpers.Truncate(method, 40),-40} base: {AnalyzerHelpers.FormatNs(baseNs),-12} compare: {AnalyzerHelpers.FormatNs(compareNs),-12} {changePercent:+0.0;-0.0}%");
        }

        Console.WriteLine(new string('-', 100));

        if (regressionCount > 0)
        {
            Console.WriteLine($"Result: FAIL ({regressionCount} regression{(regressionCount > 1 ? "s" : "")})");
            return 1;
        }

        Console.WriteLine("Result: PASS");
        return 0;
    }
}
