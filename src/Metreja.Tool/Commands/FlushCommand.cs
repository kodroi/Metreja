using System.CommandLine;

namespace Metreja.Tool.Commands;

public static class FlushCommand
{
    public static Command Create()
    {
        var pidOption = new Option<int>("--pid", "-p")
        {
            Description = "PID of the profiled process to flush",
            Required = true
        };

        var command = new Command("flush", "Trigger manual stats flush on a running profiled process");
        command.Options.Add(pidOption);

        command.SetAction((parseResult, _) =>
        {
            var pid = parseResult.GetValue(pidOption);

            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Error: flush command is only supported on Windows.");
                return Task.FromResult(1);
            }

            var eventName = $"MetrejaFlush_{pid}";

            try
            {
                using var handle = EventWaitHandle.OpenExisting(eventName);
                handle.Set();
                Console.WriteLine($"Flush signaled for PID {pid}");
                return Task.FromResult(0);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Console.Error.WriteLine(
                    $"Error: No profiled process found with PID {pid}. " +
                    "Ensure the process is running with stats events enabled (method_stats or exception_stats).");
                return Task.FromResult(1);
            }
            catch (UnauthorizedAccessException)
            {
                Console.Error.WriteLine(
                    $"Error: Access denied to flush event for PID {pid}. " +
                    "Try running with elevated privileges.");
                return Task.FromResult(1);
            }
        });

        return command;
    }
}
