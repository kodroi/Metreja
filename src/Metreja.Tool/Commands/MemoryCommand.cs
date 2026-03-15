using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class MemoryCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var topOption = new Option<int>("--top") { Description = "Number of allocation types to show", DefaultValueFactory = _ => 20 };
        var filterOption = new Option<string[]>("--filter") { Description = "Include only class names matching pattern(s)", DefaultValueFactory = _ => [] };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };
        formatOption.AcceptOnlyFromAmong("text", "json");

        var command = new Command("memory", "Show GC summary and allocation hotspots by class");
        command.Arguments.Add(fileArg);
        command.Options.Add(topOption);
        command.Options.Add(filterOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var top = parseResult.GetValue(topOption);
            var filters = parseResult.GetValue(filterOption)!;
            var format = parseResult.GetValue(formatOption)!;

            return await MemoryAnalyzer.AnalyzeAsync(file, top, filters, format);
        });

        return command;
    }
}
