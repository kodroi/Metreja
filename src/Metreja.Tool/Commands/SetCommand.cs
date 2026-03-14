using System.CommandLine;
using Metreja.Tool.Config;

namespace Metreja.Tool.Commands;

public static class SetCommand
{
    public static Command Create()
    {
        var sessionOption = SharedOptions.SessionOption();

        var command = new Command("set", "Set session configuration values");

        command.Subcommands.Add(CreateMetadataCommand(sessionOption));
        command.Subcommands.Add(CreateOutputCommand(sessionOption));
        command.Subcommands.Add(CreateMaxEventsCommand(sessionOption));
        command.Subcommands.Add(CreateComputeDeltasCommand(sessionOption));
        command.Subcommands.Add(CreateEventsCommand(sessionOption));
        command.Subcommands.Add(CreateStatsFlushIntervalCommand(sessionOption));

        return command;
    }

    private static Command CreateMetadataCommand(Option<string> sessionOption)
    {
        var scenarioArg = new Argument<string?>("scenario") { Description = "Scenario name", Arity = ArgumentArity.ZeroOrOne };

        var command = new Command("metadata", "Set metadata values");
        command.Options.Add(sessionOption);
        command.Arguments.Add(scenarioArg);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var scenario = parseResult.GetValue(scenarioArg);

            await SetConfigPropertyAsync(session, config =>
            {
                var metadata = config.Metadata;
                if (scenario is not null) metadata = metadata with { Scenario = scenario };
                return config with { Metadata = metadata };
            }, "metadata");
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

            await SetConfigPropertyAsync(session,
                config => config with { Output = config.Output with { Path = path } },
                $"output path to: {path}");
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

            await SetConfigPropertyAsync(session,
                config => config with { Instrumentation = config.Instrumentation with { MaxEvents = value } },
                $"max-events to: {value}");
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

            await SetConfigPropertyAsync(session,
                config => config with { Instrumentation = config.Instrumentation with { ComputeDeltas = value } },
                $"compute-deltas to: {value}");
        });

        return command;
    }

    private static readonly HashSet<string> ValidEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "enter", "leave", "exception", "gc_start", "gc_end", "alloc_by_class", "method_stats", "exception_stats"
    };

    private static Command CreateEventsCommand(Option<string> sessionOption)
    {
        var eventsArg = new Argument<string[]>("events")
        {
            Description = "Event types to enable (e.g. enter leave method_stats)",
            Arity = ArgumentArity.OneOrMore
        };

        var command = new Command("events", "Set enabled event types");
        command.Options.Add(sessionOption);
        command.Arguments.Add(eventsArg);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var events = parseResult.GetValue(eventsArg)!;

            var invalid = events.Where(e => !ValidEventTypes.Contains(e)).ToList();
            if (invalid.Count > 0)
            {
                Console.Error.WriteLine($"Unknown event type(s): {string.Join(", ", invalid)}");
                Console.Error.WriteLine($"Valid types: {string.Join(", ", ValidEventTypes.Order())}");
                return 1;
            }

            var eventList = events.Select(e => e.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();

            await SetConfigPropertyAsync(session,
                config => config with { Instrumentation = config.Instrumentation with { Events = eventList } },
                $"events to: [{string.Join(", ", eventList)}]");

            return 0;
        });

        return command;
    }

    private static Command CreateStatsFlushIntervalCommand(Option<string> sessionOption)
    {
        var valueArg = new Argument<int>("seconds") { Description = "Flush interval in seconds (0 = disabled)" };

        var command = new Command("stats-flush-interval", "Set periodic stats flush interval");
        command.Options.Add(sessionOption);
        command.Arguments.Add(valueArg);

        command.SetAction(async (parseResult, _) =>
        {
            var session = parseResult.GetValue(sessionOption)!;
            var value = parseResult.GetValue(valueArg);

            if (value < 0)
            {
                Console.Error.WriteLine("Error: Interval must be >= 0");
                return 1;
            }

            await SetConfigPropertyAsync(session,
                config => config with
                {
                    Instrumentation = config.Instrumentation with { StatsFlushIntervalSeconds = value }
                },
                $"stats-flush-interval to: {value}s");

            return 0;
        });

        return command;
    }

    private static async Task SetConfigPropertyAsync(
        string session, Func<ProfilerConfig, ProfilerConfig> transform, string confirmationDetail)
    {
        var manager = ConfigManager.Default;
        var config = await manager.LoadConfigAsync(session);
        await manager.SaveConfigAsync(session, transform(config));
        Console.WriteLine($"Set {confirmationDetail} for session {session}");
    }
}
