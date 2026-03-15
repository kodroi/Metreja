using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class SummaryCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };
        formatOption.AcceptOnlyFromAmong("text", "json");

        var command = new Command("summary", "Show trace overview: events, threads, methods, duration");
        command.Arguments.Add(fileArg);
        command.Options.Add(formatOption);
        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var format = parseResult.GetValue(formatOption)!;
            return await SummaryAnalyzer.AnalyzeAsync(file, format);
        });
        return command;
    }
}
