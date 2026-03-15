using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class TrendCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var methodOption = new Option<string>("--method") { Description = "Method pattern to track", Required = true };

        var command = new Command("trend", "Method performance trend across periodic stats flushes");
        command.Arguments.Add(fileArg);
        command.Options.Add(methodOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var method = parseResult.GetValue(methodOption)!;
            await TrendAnalyzer.AnalyzeAsync(file, method);
        });

        return command;
    }
}
