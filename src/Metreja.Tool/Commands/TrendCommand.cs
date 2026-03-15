using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class TrendCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var methodOption = new Option<string>("--method") { Description = "Method pattern to track", Required = true };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };
        formatOption.AcceptOnlyFromAmong("text", "json");

        var command = new Command("trend", "Method performance trend across periodic stats flushes");
        command.Arguments.Add(fileArg);
        command.Options.Add(methodOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var method = parseResult.GetValue(methodOption)!;
            var format = parseResult.GetValue(formatOption)!;
            return await TrendAnalyzer.AnalyzeAsync(file, method, format);
        });

        return command;
    }
}
