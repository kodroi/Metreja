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
        var topOption = new Option<int>("--top") { Description = "Limit output to top N methods", DefaultValueFactory = _ => 0 };
        var sortOption = new Option<string>("--sort") { Description = "Sort by: inclusive, self, calls, or percent", DefaultValueFactory = _ => "inclusive" };
        sortOption.AcceptOnlyFromAmong("inclusive", "self", "calls", "percent");
        var filterOption = new Option<string[]>("--filter") { Description = "Filter methods by name substring", Arity = ArgumentArity.ZeroOrMore };

        var command = new Command("analyze-diff", "Compare two NDJSON profiling outputs");
        command.Arguments.Add(baseArg);
        command.Arguments.Add(compareArg);
        command.Options.Add(formatOption);
        command.Options.Add(topOption);
        command.Options.Add(sortOption);
        command.Options.Add(filterOption);

        command.SetAction(async (parseResult, _) =>
        {
            var basePath = parseResult.GetValue(baseArg)!;
            var comparePath = parseResult.GetValue(compareArg)!;
            var format = parseResult.GetValue(formatOption)!;
            var top = parseResult.GetValue(topOption);
            var sort = parseResult.GetValue(sortOption)!;
            var filters = parseResult.GetValue(filterOption);

            return await DiffAnalyzer.AnalyzeAsync(basePath, comparePath, format, top, sort, filters);
        });

        return command;
    }
}
