using System.Globalization;
using System.Text.Json;

namespace Metreja.Tool.Analysis;

public static class DiffAnalyzer
{
    public static async Task<int> AnalyzeAsync(
        string basePath, string comparePath,
        string format = "text", int top = 0,
        string sort = "inclusive", string[]? filters = null,
        TextWriter? output = null)
    {
        output ??= Console.Out;

        if (!EventReader.ValidateFileExists(basePath, "Base file"))
            return 1;
        if (!EventReader.ValidateFileExists(comparePath, "Compare file"))
            return 1;

        var baseMetrics = await EventReader.CollectMethodMetricsAsync(basePath);
        var compareMetrics = await EventReader.CollectMethodMetricsAsync(comparePath);

        var allMethods = baseMetrics.Keys.Union(compareMetrics.Keys);

        if (filters is { Length: > 0 })
            allMethods = allMethods.Where(m => filters.Any(f => m.Contains(f, StringComparison.OrdinalIgnoreCase)));

        var sorted = sort switch
        {
            "self" => allMethods.OrderByDescending(k =>
                Math.Abs(compareMetrics.GetValueOrDefault(k, MethodMetrics.Empty).SelfNs -
                         baseMetrics.GetValueOrDefault(k, MethodMetrics.Empty).SelfNs)),
            "calls" => allMethods.OrderByDescending(k =>
                Math.Abs(compareMetrics.GetValueOrDefault(k, MethodMetrics.Empty).CallCount -
                         baseMetrics.GetValueOrDefault(k, MethodMetrics.Empty).CallCount)),
            "percent" => allMethods.OrderByDescending(k =>
            {
                var b = baseMetrics.GetValueOrDefault(k, MethodMetrics.Empty).InclusiveNs;
                var c = compareMetrics.GetValueOrDefault(k, MethodMetrics.Empty).InclusiveNs;
                return b > 0 ? Math.Abs((double)(c - b) / b) : 0.0;
            }),
            _ => allMethods.OrderByDescending(k =>
                Math.Abs(compareMetrics.GetValueOrDefault(k, MethodMetrics.Empty).InclusiveNs -
                         baseMetrics.GetValueOrDefault(k, MethodMetrics.Empty).InclusiveNs))
        };

        var methods = top > 0 ? sorted.Take(top) : sorted;

        if (format == "json")
            return WriteJson(methods, baseMetrics, compareMetrics, output);

        return WriteText(methods, baseMetrics, compareMetrics, output);
    }

    private static int WriteJson(
        IEnumerable<string> methods,
        Dictionary<string, MethodMetrics> baseMetrics,
        Dictionary<string, MethodMetrics> compareMetrics,
        TextWriter output)
    {
        var result = new
        {
            methods = methods.Select(method =>
            {
                var b = baseMetrics.GetValueOrDefault(method, MethodMetrics.Empty);
                var c = compareMetrics.GetValueOrDefault(method, MethodMetrics.Empty);
                return new
                {
                    method,
                    baseSelfNs = b.SelfNs,
                    compareSelfNs = c.SelfNs,
                    selfDeltaNs = c.SelfNs - b.SelfNs,
                    selfChangePercent = b.SelfNs > 0 ? (double)(c.SelfNs - b.SelfNs) / b.SelfNs * 100 : 0.0,
                    baseInclusiveNs = b.InclusiveNs,
                    compareInclusiveNs = c.InclusiveNs,
                    inclusiveDeltaNs = c.InclusiveNs - b.InclusiveNs,
                    inclusiveChangePercent = b.InclusiveNs > 0 ? (double)(c.InclusiveNs - b.InclusiveNs) / b.InclusiveNs * 100 : 0.0,
                    baseCalls = b.CallCount,
                    compareCalls = c.CallCount,
                    callsDelta = c.CallCount - b.CallCount,
                    callsChangePercent = b.CallCount > 0 ? (double)(c.CallCount - b.CallCount) / b.CallCount * 100 : 0.0,
                    hasStats = b.HasStats || c.HasStats
                };
            }).ToList(),
            baseMethodCount = baseMetrics.Count,
            compareMethodCount = compareMetrics.Count
        };

        output.WriteLine(JsonSerializer.Serialize(result, JsonOutputOptions.Default));
        return 0;
    }

    private static int WriteText(
        IEnumerable<string> methods,
        Dictionary<string, MethodMetrics> baseMetrics,
        Dictionary<string, MethodMetrics> compareMetrics,
        TextWriter output)
    {
        var methodList = methods.ToList();
        var anyHasStats = methodList.Any(m =>
            baseMetrics.GetValueOrDefault(m, MethodMetrics.Empty).HasStats ||
            compareMetrics.GetValueOrDefault(m, MethodMetrics.Empty).HasStats);

        output.WriteLine("Method Timing Diff (base -> compare):");

        if (anyHasStats)
        {
            var width = 130;
            output.WriteLine(new string('-', width));
            output.WriteLine(
                $"{"#",5} {"Method",-40} {"Self \u0394",12} {"Self %",8} {"Incl \u0394",12} {"Incl %",8} {"Calls \u0394",10} {"Calls %",9}");
            output.WriteLine(new string('-', width));

            var row = 0;
            foreach (var method in methodList)
            {
                row++;
                var b = baseMetrics.GetValueOrDefault(method, MethodMetrics.Empty);
                var c = compareMetrics.GetValueOrDefault(method, MethodMetrics.Empty);
                var hasStats = b.HasStats || c.HasStats;

                var selfDelta = c.SelfNs - b.SelfNs;
                var inclDelta = c.InclusiveNs - b.InclusiveNs;
                var callsDelta = c.CallCount - b.CallCount;

                var selfDeltaStr = hasStats ? FormatDelta(selfDelta) : "-";
                var selfPctStr = hasStats ? FormatPercent(b.SelfNs, selfDelta) : "-";
                var inclDeltaStr = FormatDelta(inclDelta);
                var inclPctStr = FormatPercent(b.InclusiveNs, inclDelta);
                var callsDeltaStr = hasStats ? FormatCallsDelta(callsDelta) : "-";
                var callsPctStr = hasStats ? FormatPercent(b.CallCount, callsDelta) : "-";

                output.WriteLine(
                    $"{row,5} {FormatUtils.Truncate(method, 40),-40} {selfDeltaStr,12} {selfPctStr,8} {inclDeltaStr,12} {inclPctStr,8} {callsDeltaStr,10} {callsPctStr,9}");
            }

            output.WriteLine(new string('-', width));
        }
        else
        {
            // Leave-only data: simplified output without self/calls columns
            var width = 100;
            output.WriteLine(new string('-', width));
            output.WriteLine($"{"Method",-50} {"Base",12} {"Compare",12} {"Delta",12} {"Change",10}");
            output.WriteLine(new string('-', width));

            foreach (var method in methodList)
            {
                var b = baseMetrics.GetValueOrDefault(method, MethodMetrics.Empty);
                var c = compareMetrics.GetValueOrDefault(method, MethodMetrics.Empty);
                var delta = c.InclusiveNs - b.InclusiveNs;
                var deltaPrefix = delta >= 0 ? "+" : "";
                var changeStr = b.InclusiveNs > 0
                    ? string.Format(CultureInfo.InvariantCulture, "{0:P0}", (double)delta / b.InclusiveNs)
                    : c.InclusiveNs > 0 ? "new" : "";
                output.WriteLine(
                    $"{FormatUtils.Truncate(method, 50),-50} {FormatUtils.FormatNs(b.InclusiveNs),12} {FormatUtils.FormatNs(c.InclusiveNs),12} {deltaPrefix + FormatUtils.FormatNs(Math.Abs(delta)),12} {changeStr,10}");
            }

            output.WriteLine(new string('-', width));
        }

        output.WriteLine($"Methods in base: {baseMetrics.Count}, in compare: {compareMetrics.Count}");
        return 0;
    }

    private static string FormatDelta(long deltaNs)
    {
        var prefix = deltaNs >= 0 ? "+" : "";
        return prefix + FormatUtils.FormatNs(Math.Abs(deltaNs));
    }

    private static string FormatCallsDelta(long delta)
    {
        return delta >= 0 ? $"+{delta}" : delta.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(long baseValue, long delta)
    {
        if (baseValue == 0)
            return delta != 0 ? "new" : "0%";
        return string.Format(CultureInfo.InvariantCulture, "{0:+0%;-0%;0%}", (double)delta / baseValue);
    }
}
