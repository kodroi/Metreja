using System.CommandLine;
using Metreja.Cli.Commands;

var rootCommand = new RootCommand("Metreja - .NET Call-Path Profiler CLI");

rootCommand.Subcommands.Add(InitCommand.Create());
rootCommand.Subcommands.Add(AddCommand.Create());
rootCommand.Subcommands.Add(RemoveCommand.Create());
rootCommand.Subcommands.Add(ClearFiltersCommand.Create());
rootCommand.Subcommands.Add(SetCommand.Create());
rootCommand.Subcommands.Add(ValidateCommand.Create());
rootCommand.Subcommands.Add(GenerateEnvCommand.Create());
rootCommand.Subcommands.Add(ClearCommand.Create());
rootCommand.Subcommands.Add(AnalyzeDiffCommand.Create());
rootCommand.Subcommands.Add(HotspotsCommand.Create());
rootCommand.Subcommands.Add(CallTreeCommand.Create());
rootCommand.Subcommands.Add(CallersCommand.Create());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
