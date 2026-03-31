using System.CommandLine;
using Metreja.Tool.Export;

namespace Metreja.Tool.Commands;

public static class ExportCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var formatOption = new Option<string>("--format") { Description = "Export format", DefaultValueFactory = _ => "speedscope" };
        formatOption.AcceptOnlyFromAmong("speedscope", "csv");
        var outputOption = new Option<string?>("--output") { Description = "Output file path (default: auto-generated based on format)" };

        var command = new Command("export", "Convert traces to external formats (speedscope, csv)");
        command.Arguments.Add(fileArg);
        command.Options.Add(formatOption);
        command.Options.Add(outputOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var format = parseResult.GetValue(formatOption)!;

            switch (format)
            {
                case "speedscope":
                {
                    var output = parseResult.GetValue(outputOption) ?? $"{file}.speedscope.json";
                    await SpeedscopeExporter.ExportAsync(file, output);
                    return 0;
                }
                case "csv":
                {
                    var output = parseResult.GetValue(outputOption) ?? $"{file}.csv";
                    await CsvExporter.ExportAsync(file, output);
                    return 0;
                }
                default:
                    Console.Error.WriteLine($"Unsupported format: {format}. Supported: speedscope, csv");
                    return 1;
            }
        });

        return command;
    }
}
