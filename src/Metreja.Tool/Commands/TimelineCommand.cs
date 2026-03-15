using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class TimelineCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var tidOption = new Option<long?>("--tid") { Description = "Filter by thread ID" };
        var eventTypeOption = new Option<string?>("--event-type") { Description = "Filter by event type" };
        var methodOption = new Option<string?>("--method") { Description = "Filter by method pattern" };
        var topOption = new Option<int>("--top") { Description = "Maximum events to show", DefaultValueFactory = _ => 100 };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };

        var command = new Command("timeline", "Chronological event listing with filtering");
        command.Arguments.Add(fileArg);
        command.Options.Add(tidOption);
        command.Options.Add(eventTypeOption);
        command.Options.Add(methodOption);
        command.Options.Add(topOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var tid = parseResult.GetValue(tidOption);
            var eventType = parseResult.GetValue(eventTypeOption);
            var method = parseResult.GetValue(methodOption);
            var top = parseResult.GetValue(topOption);
            var format = parseResult.GetValue(formatOption)!;
            return await TimelineAnalyzer.AnalyzeAsync(file, tid, eventType, method, top, format);
        });

        return command;
    }
}
