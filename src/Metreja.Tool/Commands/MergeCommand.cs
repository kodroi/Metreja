using System.CommandLine;
using System.Text.Json;

namespace Metreja.Tool.Commands;

public static class MergeCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<string[]>("files")
        {
            Description = "NDJSON trace files to merge",
            Arity = ArgumentArity.OneOrMore
        };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path",
            Required = true
        };

        var command = new Command("merge", "Combine multiple trace files into one sorted by timestamp");
        command.Arguments.Add(filesArg);
        command.Options.Add(outputOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var files = parseResult.GetValue(filesArg)!;
            var output = parseResult.GetValue(outputOption)!;

            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"Error: File not found: {file}");
                    return 1;
                }
            }

            var events = new List<(long TsNs, string Line)>();
            var skippedCount = 0;

            foreach (var file in files)
            {
                await foreach (var line in File.ReadLinesAsync(file, cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var tsNs = doc.RootElement.TryGetProperty("tsNs", out var ts)
                            ? ts.GetInt64()
                            : 0;
                        events.Add((tsNs, line));
                    }
                    catch (JsonException)
                    {
                        Console.Error.WriteLine($"Warning: Skipping malformed JSON line");
                        skippedCount++;
                    }
                }
            }

            events.Sort((a, b) => a.TsNs.CompareTo(b.TsNs));

            await File.WriteAllLinesAsync(output, events.Select(e => e.Line), cancellationToken);

            Console.WriteLine($"Merged {events.Count} events from {files.Length} files into {output}");
            if (skippedCount > 0)
            {
                Console.WriteLine($"Skipped {skippedCount} malformed lines");
            }

            return 0;
        });

        return command;
    }
}
