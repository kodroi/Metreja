using System.CommandLine;
using Metreja.Cli.Analysis;

namespace Metreja.Cli.Commands;

public static class CallersCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var methodOption = new Option<string>("--method") { Description = "Method name or pattern to match", Required = true };
        var topOption = new Option<int>("--top") { Description = "Number of callers to show", DefaultValueFactory = _ => 20 };

        var command = new Command("callers", "Show which methods call a specific method");
        command.Arguments.Add(fileArg);
        command.Options.Add(methodOption);
        command.Options.Add(topOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var method = parseResult.GetValue(methodOption)!;
            var top = parseResult.GetValue(topOption);

            await CallersAnalyzer.AnalyzeAsync(file, method, top);
        });

        return command;
    }
}
