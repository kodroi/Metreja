using System.CommandLine;
using Metreja.Tool.Analysis;

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

            var result = await NdjsonMerger.MergeFilesAsync(files, output, cancellationToken);

            Console.WriteLine($"Merged {result.EventCount} events from {files.Length} files into {output}");
            if (result.SkippedCount > 0)
            {
                Console.WriteLine($"Skipped {result.SkippedCount} malformed lines");
            }

            return 0;
        });

        return command;
    }
}
