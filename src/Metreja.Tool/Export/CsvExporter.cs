using System.Globalization;
using System.Text;
using System.Text.Json;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Export;

internal static class CsvExporter
{
    public static async Task ExportAsync(string inputPath, string outputPath)
    {
        if (!AnalyzerHelpers.ValidateFileExists(inputPath, "File"))
            return;

        var hasEnterLeave = false;
        var hasMethodStats = false;

        await foreach (var (eventType, _) in AnalyzerHelpers.StreamEventsAsync(inputPath))
        {
            if (eventType is "enter" or "leave")
            {
                hasEnterLeave = true;
                break;
            }

            if (eventType == "method_stats")
            {
                hasMethodStats = true;
                break;
            }
        }

        if (hasEnterLeave)
        {
            await ExportEnterLeaveAsync(inputPath, outputPath);
        }
        else if (hasMethodStats)
        {
            await ExportMethodStatsAsync(inputPath, outputPath);
        }
        else
        {
            Console.Error.WriteLine("Error: no enter/leave or method_stats events found in trace.");
        }
    }

    private static async Task ExportEnterLeaveAsync(string inputPath, string outputPath)
    {
        var rowCount = 0;

        await using var stream = File.Create(outputPath);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        await writer.WriteLineAsync("tsNs,event,tid,depth,ns,cls,method,deltaNs,async");

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(inputPath))
        {
            if (eventType is not "enter" and not "leave")
                continue;

            var tsNs = root.TryGetProperty("tsNs", out var ts) ? ts.GetInt64() : 0;
            var tid = root.TryGetProperty("tid", out var t) ? t.GetInt64() : 0;
            var depth = root.TryGetProperty("depth", out var dp) ? dp.GetInt32() : 0;
            var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
            var deltaNs = root.TryGetProperty("deltaNs", out var d) ? d.GetInt64() : 0;
            var isAsync = root.TryGetProperty("async", out var asyncProp) &&
                          asyncProp.ValueKind == JsonValueKind.True;

            await writer.WriteLineAsync(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                tsNs,
                eventType,
                tid,
                depth,
                EscapeCsv(ns),
                EscapeCsv(cls),
                EscapeCsv(m),
                eventType == "leave" ? deltaNs.ToString(CultureInfo.InvariantCulture) : "",
                isAsync ? "true" : "false"));

            rowCount++;
        }

        Console.WriteLine($"Exported {rowCount} events to {outputPath}");
    }

    private static async Task ExportMethodStatsAsync(string inputPath, string outputPath)
    {
        var rowCount = 0;

        await using var stream = File.Create(outputPath);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        await writer.WriteLineAsync("method,callCount,totalSelfNs,maxSelfNs,totalInclusiveNs,maxInclusiveNs");

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(inputPath))
        {
            if (eventType != "method_stats")
                continue;

            var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
            var method = AnalyzerHelpers.BuildMethodKey(ns, cls, m);
            var callCount = root.TryGetProperty("callCount", out var cc) ? cc.GetInt64() : 0;
            var totalSelfNs = root.TryGetProperty("totalSelfNs", out var sn) ? sn.GetInt64() : 0;
            var maxSelfNs = root.TryGetProperty("maxSelfNs", out var smx) ? smx.GetInt64() : 0;
            var totalInclusiveNs = root.TryGetProperty("totalInclusiveNs", out var inc) ? inc.GetInt64() : 0;
            var maxInclusiveNs = root.TryGetProperty("maxInclusiveNs", out var imx) ? imx.GetInt64() : 0;

            await writer.WriteLineAsync(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5}",
                EscapeCsv(method),
                callCount,
                totalSelfNs,
                maxSelfNs,
                totalInclusiveNs,
                maxInclusiveNs));

            rowCount++;
        }

        Console.WriteLine($"Exported {rowCount} method_stats rows to {outputPath}");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
