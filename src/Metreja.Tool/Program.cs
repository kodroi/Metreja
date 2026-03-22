using System.CommandLine;
using Metreja.Tool.Analytics;
using Metreja.Tool.Commands;

var rootCommand = new RootCommand("Metreja - .NET Call-Path Profiler CLI");

rootCommand.Subcommands.Add(InitCommand.Create());
rootCommand.Subcommands.Add(AddCommand.Create());
rootCommand.Subcommands.Add(RemoveCommand.Create());
rootCommand.Subcommands.Add(ClearFiltersCommand.Create());
rootCommand.Subcommands.Add(SetCommand.Create());
rootCommand.Subcommands.Add(ValidateCommand.Create());
rootCommand.Subcommands.Add(GenerateEnvCommand.Create());
rootCommand.Subcommands.Add(RunCommand.Create());
rootCommand.Subcommands.Add(ClearCommand.Create());
rootCommand.Subcommands.Add(AnalyzeDiffCommand.Create());
rootCommand.Subcommands.Add(HotspotsCommand.Create());
rootCommand.Subcommands.Add(CallTreeCommand.Create());
rootCommand.Subcommands.Add(CallersCommand.Create());
rootCommand.Subcommands.Add(MemoryCommand.Create());
rootCommand.Subcommands.Add(SummaryCommand.Create());
rootCommand.Subcommands.Add(ExceptionsCommand.Create());
rootCommand.Subcommands.Add(TimelineCommand.Create());
rootCommand.Subcommands.Add(ThreadsCommand.Create());
rootCommand.Subcommands.Add(TrendCommand.Create());
rootCommand.Subcommands.Add(CheckCommand.Create());
rootCommand.Subcommands.Add(ListCommand.Create());
rootCommand.Subcommands.Add(MergeCommand.Create());
rootCommand.Subcommands.Add(ExportCommand.Create());
rootCommand.Subcommands.Add(ReportCommand.Create());
rootCommand.Subcommands.Add(FlushCommand.Create());

TelemetryService.Initialize();

var parseResult = rootCommand.Parse(args);
var exitCode = await parseResult.InvokeAsync();

var commandName = parseResult.CommandResult.Command.Name;
TelemetryService.TrackCommand(commandName, args, exitCode);

await Metreja.Tool.UpdateChecker.CheckForUpdateAsync();
await TelemetryService.FlushAndDisposeAsync();

return exitCode;
