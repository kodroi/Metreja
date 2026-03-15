using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class CallTreeCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var methodOption = new Option<string>("--method") { Description = "Method name or pattern to match", Required = true };
        var tidOption = new Option<long?>("--tid") { Description = "Filter by thread ID" };
        var occurrenceOption = new Option<int>("--occurrence") { Description = "Which occurrence to show (1 = slowest)", DefaultValueFactory = _ => 1 };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };

        var command = new Command("calltree", "Show the call tree for a specific method invocation");
        command.Arguments.Add(fileArg);
        command.Options.Add(methodOption);
        command.Options.Add(tidOption);
        command.Options.Add(occurrenceOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var method = parseResult.GetValue(methodOption)!;
            var tid = parseResult.GetValue(tidOption);
            var occ = parseResult.GetValue(occurrenceOption);
            var format = parseResult.GetValue(formatOption)!;

            return await CallTreeAnalyzer.AnalyzeAsync(file, method, tid, occ, format);
        });

        return command;
    }
}
