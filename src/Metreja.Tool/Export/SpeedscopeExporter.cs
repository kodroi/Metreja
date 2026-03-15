using System.Text.Json;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Export;

internal static class SpeedscopeExporter
{
    public static async Task ExportAsync(string inputPath, string outputPath)
    {
        if (!AnalyzerHelpers.ValidateFileExists(inputPath, "File"))
            return;

        var frameIndex = new Dictionary<string, int>();
        var frameNames = new List<string>();
        var threadEvents = new Dictionary<long, List<SpeedscopeEvent>>();
        var threadMinTs = new Dictionary<long, long>();
        var threadMaxTs = new Dictionary<long, long>();
        var scenario = "";

        await foreach (var (eventType, root) in AnalyzerHelpers.StreamEventsAsync(inputPath))
        {
            if (eventType == "session_metadata")
            {
                if (root.TryGetProperty("scenario", out var sc))
                    scenario = sc.GetString() ?? "";
                continue;
            }

            if (eventType is not "enter" and not "leave")
                continue;

            var tid = root.TryGetProperty("tid", out var t) ? t.GetInt64() : 0;
            var tsNs = root.TryGetProperty("tsNs", out var ts) ? ts.GetInt64() : 0;

            var (ns, cls, m) = AnalyzerHelpers.ExtractMethodInfo(root);
            var key = AnalyzerHelpers.BuildMethodKey(ns, cls, m);

            if (!frameIndex.TryGetValue(key, out var idx))
            {
                idx = frameNames.Count;
                frameIndex[key] = idx;
                frameNames.Add(key);
            }

            if (!threadEvents.TryGetValue(tid, out var evList))
            {
                evList = [];
                threadEvents[tid] = evList;
                threadMinTs[tid] = long.MaxValue;
                threadMaxTs[tid] = long.MinValue;
            }

            evList.Add(new SpeedscopeEvent
            {
                Type = eventType == "enter" ? "O" : "C",
                Frame = idx,
                At = tsNs
            });

            if (tsNs < threadMinTs[tid]) threadMinTs[tid] = tsNs;
            if (tsNs > threadMaxTs[tid]) threadMaxTs[tid] = tsNs;
        }

        await using var stream = File.Create(outputPath);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("$schema", "https://www.speedscope.app/file-format-schema.json");

        writer.WriteStartObject("shared");
        writer.WriteStartArray("frames");
        foreach (var name in frameNames)
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartArray("profiles");
        foreach (var (tid, events) in threadEvents.OrderBy(kv => kv.Key))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "evented");
            writer.WriteString("name", $"Thread {tid}");
            writer.WriteString("unit", "nanoseconds");
            writer.WriteNumber("startValue", threadMinTs[tid]);
            writer.WriteNumber("endValue", threadMaxTs[tid]);

            writer.WriteStartArray("events");
            foreach (var ev in events)
            {
                writer.WriteStartObject();
                writer.WriteString("type", ev.Type);
                writer.WriteNumber("frame", ev.Frame);
                writer.WriteNumber("at", ev.At);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        var traceName = string.IsNullOrEmpty(scenario)
            ? "Metreja trace"
            : $"Metreja trace - {scenario}";
        writer.WriteString("name", traceName);
        writer.WriteString("exporter", "Metreja CLI");

        writer.WriteEndObject();

        Console.WriteLine($"Exported {frameNames.Count} frames, {threadEvents.Count} thread(s) to {outputPath}");
    }

    private sealed class SpeedscopeEvent
    {
        public string Type { get; set; } = "";
        public int Frame { get; set; }
        public long At { get; set; }
    }
}
