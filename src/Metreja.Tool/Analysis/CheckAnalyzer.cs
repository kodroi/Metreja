using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class CheckAnalyzer
{
    public static async Task<int> AnalyzeAsync(string basePath, string comparePath, double threshold, string format = "text", TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(basePath, "Base file"))
            return 1;
        if (!EventReader.ValidateFileExists(comparePath, "Compare file"))
            return 1;

        var baseTimings = await EventReader.CollectMethodTimingsAsync(basePath);
        var compareTimings = await EventReader.CollectMethodTimingsAsync(comparePath);

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

            output.WriteLine(JsonSerializer.Serialize(result, JsonOutputOptions.Default));
            return regressionCount > 0 ? 1 : 0;
        }

        output.WriteLine($"Performance check (threshold: {threshold:F1}%)");
        output.WriteLine(new string('-', 100));

        foreach (var (method, status, baseNs, compareNs, changePercent) in methodEntries)
        {
            output.WriteLine(
                $"{status,-12} {FormatUtils.Truncate(method, 40),-40} base: {FormatUtils.FormatNs(baseNs),-12} compare: {FormatUtils.FormatNs(compareNs),-12} {changePercent:+0.0;-0.0}%");
        }

        output.WriteLine(new string('-', 100));

        if (regressionCount > 0)
        {
            output.WriteLine($"Result: FAIL ({regressionCount} regression{(regressionCount > 1 ? "s" : "")})");
            return 1;
        }

        output.WriteLine("Result: PASS");
        return 0;
    }
}
