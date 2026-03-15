using System.CommandLine;
using Metreja.Tool.Analysis;

namespace Metreja.Tool.Commands;

public static class HotspotsCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<string>("file") { Description = "NDJSON trace file path" };
        var topOption = new Option<int>("--top") { Description = "Number of methods to show", DefaultValueFactory = _ => 20 };
        var minMsOption = new Option<double>("--min-ms") { Description = "Minimum time threshold in milliseconds", DefaultValueFactory = _ => 0.0 };
        var sortOption = new Option<string>("--sort") { Description = "Sort by: self, inclusive, calls, or allocs", DefaultValueFactory = _ => "self" };
        var filterOption = new Option<string[]>("--filter") { Description = "Include only methods matching pattern(s) (method, class, or namespace)", DefaultValueFactory = _ => [] };
        var formatOption = new Option<string>("--format") { Description = "Output format: text or json", DefaultValueFactory = _ => "text" };
        formatOption.AcceptOnlyFromAmong("text", "json");

        var command = new Command("hotspots", "Show per-method timing hotspots with self time");
        command.Arguments.Add(fileArg);
        command.Options.Add(topOption);
        command.Options.Add(minMsOption);
        command.Options.Add(sortOption);
        command.Options.Add(filterOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, _) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var top = parseResult.GetValue(topOption);
            var minMs = parseResult.GetValue(minMsOption);
            var sort = parseResult.GetValue(sortOption)!;
            var filters = parseResult.GetValue(filterOption)!;
            var format = parseResult.GetValue(formatOption)!;

            return await HotspotsAnalyzer.AnalyzeAsync(file, top, minMs, sort, filters, format);
        });

        return command;
    }
}
