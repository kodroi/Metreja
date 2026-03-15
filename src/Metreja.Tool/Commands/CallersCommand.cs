using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class CallersCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var methodOption = new Option<string>("--method") { Description = "Method name or pattern to match", Required = true };
        var topOption = new Option<int>("--top") { Description = "Number of callers to show", DefaultValueFactory = _ => 20 };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };

        var command = new Command("callers", "Show which methods call a specific method");
        command.Arguments.Add(fileArg);
        command.Options.Add(methodOption);
        command.Options.Add(topOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var method = parseResult.GetValue(methodOption)!;
            var top = parseResult.GetValue(topOption);
            var format = parseResult.GetValue(formatOption)!;

            return await CallersAnalyzer.AnalyzeAsync(file, method, top, format);
        });

        return command;
    }
}
