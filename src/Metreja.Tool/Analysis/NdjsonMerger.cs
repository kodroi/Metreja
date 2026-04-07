using System.Text.Json;

namespace Metreja.Tool.Analysis;

internal record MergeResult(int EventCount, int SkippedCount);

internal static class NdjsonMerger
{
    public static async Task<MergeResult> MergeFilesAsync(
        IReadOnlyList<string> inputFiles,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var events = new List<(long TsNs, string Line)>();
        var skippedCount = 0;

        foreach (var file in inputFiles)
        {
            await foreach (var line in File.ReadLinesAsync(file, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    long tsNs = 0;
                    if (doc.RootElement.TryGetProperty("tsNs", out var ts) &&
                        ts.ValueKind == JsonValueKind.Number)
                    {
                        ts.TryGetInt64(out tsNs);
                    }
                    events.Add((tsNs, line));
                }
                catch (JsonException)
                {
                    skippedCount++;
                }
            }
        }

        events.Sort((a, b) => a.TsNs.CompareTo(b.TsNs));

        await File.WriteAllLinesAsync(outputPath, events.Select(e => e.Line), cancellationToken);

        return new MergeResult(events.Count, skippedCount);
    }

    public static string ComputeMergedPath(string outputPathTemplate, string sessionId)
    {
        var path = outputPathTemplate.Replace("{sessionId}", sessionId);

        var pidIndex = path.IndexOf("{pid}", StringComparison.Ordinal);
        if (pidIndex < 0)
            return path;

        var pidEnd = pidIndex + "{pid}".Length;

        // Remove {pid} plus one adjacent separator character (_ or -)
        if (pidIndex > 0 && path[pidIndex - 1] is '_' or '-')
        {
            path = path.Remove(pidIndex - 1, pidEnd - pidIndex + 1);
        }
        else if (pidEnd < path.Length && path[pidEnd] is '_' or '-')
        {
            path = path.Remove(pidIndex, pidEnd - pidIndex + 1);
        }
        else
        {
            path = path.Remove(pidIndex, pidEnd - pidIndex);
        }

        return path;
    }

    public static string[] FindSessionOutputFiles(string outputPathTemplate, string sessionId)
    {
        var globPattern = outputPathTemplate
            .Replace("{sessionId}", sessionId)
            .Replace("{pid}", "*");

        var directory = Path.GetDirectoryName(globPattern);
        var filePattern = Path.GetFileName(globPattern);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return [];

        var mergedPath = ComputeMergedPath(outputPathTemplate, sessionId);
        var fullMergedPath = Path.GetFullPath(mergedPath);

        return Directory.GetFiles(directory, filePattern)
            .Where(f => !string.Equals(Path.GetFullPath(f), fullMergedPath, StringComparison.OrdinalIgnoreCase))
            .Order()
            .ToArray();
    }
}
