using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class AnalyzeDiffCommand
{
    public static Command Create()
    {
        var baseArg = new Argument<string>("base") { Description = "Base NDJSON file path" };
        var compareArg = new Argument<string>("compare") { Description = "Comparison NDJSON file path" };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };
        formatOption.AcceptOnlyFromAmong("text", "json");

        var command = new Command("analyze-diff", "Compare two NDJSON profiling outputs");
        command.Arguments.Add(baseArg);
        command.Arguments.Add(compareArg);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var basePath = parseResult.GetValue(baseArg)!;
            var comparePath = parseResult.GetValue(compareArg)!;
            var format = parseResult.GetValue(formatOption)!;

            return await DiffAnalyzer.AnalyzeAsync(basePath, comparePath, format);
        });

        return command;
    }
}
