using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class ExceptionsCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var topOption = new Option<int>("--top") { Description = "Number of exception types to show", DefaultValueFactory = _ => 20 };
        var filterOption = new Option<string[]>("--filter") { Description = "Filter by exception type name", DefaultValueFactory = _ => [] };

        var command = new Command("exceptions", "Rank exception types by frequency with throw-site methods");
        command.Arguments.Add(fileArg);
        command.Options.Add(topOption);
        command.Options.Add(filterOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var top = parseResult.GetValue(topOption);
            var filters = parseResult.GetValue(filterOption)!;
            await ExceptionsAnalyzer.AnalyzeAsync(file, top, filters);
        });

        return command;
    }
}
