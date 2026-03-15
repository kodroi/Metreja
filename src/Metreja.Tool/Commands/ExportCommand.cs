using System.CommandLine;
using Metreja.Tool.Export;

namespace Metreja.Tool.Commands;

public static class ExportCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var formatOption = new Option<string>("--format") { Description = "Export format", DefaultValueFactory = _ => "speedscope" };
        var outputOption = new Option<string?>("--output") { Description = "Output file path (default: input + .speedscope.json)" };

        var command = new Command("export", "Convert traces to speedscope format for visualization");
        command.Arguments.Add(fileArg);
        command.Options.Add(formatOption);
        command.Options.Add(outputOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var format = parseResult.GetValue(formatOption)!;
            var output = parseResult.GetValue(outputOption) ?? $"{file}.speedscope.json";

            if (format != "speedscope")
            {
                Console.Error.WriteLine($"Unsupported format: {format}. Supported: speedscope");
                return 1;
            }

            await SpeedscopeExporter.ExportAsync(file, output);
            return 0;
        });

        return command;
    }
}
