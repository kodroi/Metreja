using System.CommandLine;
using Metreja.Cli.Analysis;

namespace Metreja.Cli.Commands;

public static class AnalyzeDiffCommand
{
    public static Command Create()
    {
        var baseArg = new Argument<string>("base") { Description = "Base NDJSON file path" };
        var compareArg = new Argument<string>("compare") { Description = "Comparison NDJSON file path" };

        var command = new Command("analyze-diff", "Compare two NDJSON profiling outputs");
        command.Arguments.Add(baseArg);
        command.Arguments.Add(compareArg);

        command.SetAction(async (parseResult, _) =>
        {
            var basePath = parseResult.GetValue(baseArg)!;
            var comparePath = parseResult.GetValue(compareArg)!;

            await DiffAnalyzer.AnalyzeAsync(basePath, comparePath);
        });

        return command;
    }
}
