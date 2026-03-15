using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class CheckCommand
{
    public static Command Create()
    {
        var baseArg = new Argument<string>("base") { Description = "Base NDJSON trace file path" };
        var compareArg = new Argument<string>("compare") { Description = "Compare NDJSON trace file path" };
        var thresholdOption = new Option<double>("--threshold") { Description = "Regression threshold percentage", DefaultValueFactory = _ => 10.0 };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };

        var command = new Command("check", "CI regression gate: compare traces, exit non-zero on regression");
        command.Arguments.Add(baseArg);
        command.Arguments.Add(compareArg);
        command.Options.Add(thresholdOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var basePath = parseResult.GetValue(baseArg)!;
            var comparePath = parseResult.GetValue(compareArg)!;
            var threshold = parseResult.GetValue(thresholdOption);
            var format = parseResult.GetValue(formatOption)!;
            return await CheckAnalyzer.AnalyzeAsync(basePath, comparePath, threshold, format);
        });

        return command;
    }
}
