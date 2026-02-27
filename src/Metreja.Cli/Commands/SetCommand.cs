using System.CommandLine;
using Metreja.Cli.Config;

namespace Metreja.Cli.Commands;

public static class SetCommand
{
    public static Command Create()
    {
        var sessionOption = new Option<string>("--session", "-s")
        {
            Description = "Session ID",
            Required = true
        };

        var command = new Command("set", "Set session configuration values");

        command.Subcommands.Add(CreateMetadataCommand(sessionOption));
        command.Subcommands.Add(CreateOutputCommand(sessionOption));
        command.Subcommands.Add(CreateModeCommand(sessionOption));
        command.Subcommands.Add(CreateMaxEventsCommand(sessionOption));
        command.Subcommands.Add(CreateComputeDeltasCommand(sessionOption));

        return command;
    }

    private static Command CreateMetadataCommand(Option<string> sessionOption)
    {
        var scenarioOption = new Option<string>("--scenario") { Description = "Scenario name" };
        var runIdOption = new Option<string>("--run-id") { Description = "Run ID" };

        var command = new Command("metadata", "Set metadata values");
        command.Options.Add(sessionOption);
        command.Options.Add(scenarioOption);
        command.Options.Add(runIdOption);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            var metadata = config.Metadata;
            var scenario = parseResult.GetValue(scenarioOption);
            var runId = parseResult.GetValue(runIdOption);
            if (scenario is not null) metadata = metadata with { Scenario = scenario };
            if (runId is not null) metadata = metadata with { RunId = runId };

            await manager.SaveConfigAsync(session, config with { Metadata = metadata });
            Console.WriteLine($"Updated metadata for session {session}");
        });

        return command;
    }

    private static Command CreateOutputCommand(Option<string> sessionOption)
    {
        var pathArg = new Argument<string>("path") { Description = "Output file path pattern" };

        var command = new Command("output", "Set output path");
        command.Options.Add(sessionOption);
        command.Arguments.Add(pathArg);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var path = parseResult.GetValue(pathArg)!;
            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            await manager.SaveConfigAsync(session, config with { Output = config.Output with { Path = path } });
            Console.WriteLine($"Set output path to: {path}");
        });

        return command;
    }

    private static Command CreateModeCommand(Option<string> sessionOption)
    {
        var modeArg = new Argument<string>("mode") { Description = "Instrumentation mode (elt3)" };

        var command = new Command("mode", "Set instrumentation mode");
        command.Options.Add(sessionOption);
        command.Arguments.Add(modeArg);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var mode = parseResult.GetValue(modeArg)!;
            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            await manager.SaveConfigAsync(session,
                config with { Instrumentation = config.Instrumentation with { Mode = mode } });
            Console.WriteLine($"Set mode to: {mode}");
        });

        return command;
    }

    private static Command CreateMaxEventsCommand(Option<string> sessionOption)
    {
        var valueArg = new Argument<int>("value") { Description = "Maximum events (0 = unlimited)" };

        var command = new Command("max-events", "Set maximum event count");
        command.Options.Add(sessionOption);
        command.Arguments.Add(valueArg);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var value = parseResult.GetValue(valueArg);
            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            await manager.SaveConfigAsync(session,
                config with { Instrumentation = config.Instrumentation with { MaxEvents = value } });
            Console.WriteLine($"Set max-events to: {value}");
        });

        return command;
    }

    private static Command CreateComputeDeltasCommand(Option<string> sessionOption)
    {
        var valueArg = new Argument<bool>("value") { Description = "Enable delta timing" };

        var command = new Command("compute-deltas", "Enable or disable delta timing");
        command.Options.Add(sessionOption);
        command.Arguments.Add(valueArg);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var value = parseResult.GetValue(valueArg);
            var manager = new ConfigManager();
            var config = await manager.LoadConfigAsync(session);

            await manager.SaveConfigAsync(session,
                config with { Instrumentation = config.Instrumentation with { ComputeDeltas = value } });
            Console.WriteLine($"Set compute-deltas to: {value}");
        });

        return command;
    }
}
