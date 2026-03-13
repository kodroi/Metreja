using System.CommandLine;
using System.Runtime.InteropServices;

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

            if (pid <= 0)
            {
                Console.Error.WriteLine($"Error: --pid must be a positive integer, got {pid}.");
                return Task.FromResult(1);
            }

            if (OperatingSystem.IsWindows())
                return Task.FromResult(FlushWindows(pid));
            if (OperatingSystem.IsMacOS())
                return Task.FromResult(FlushMacOS(pid));

            Console.Error.WriteLine("Error: flush command is only supported on Windows and macOS.");
            return Task.FromResult(1);
        });

        return command;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int FlushWindows(int pid)
    {
        var eventName = $"MetrejaFlush_{pid}";

        try
        {
            using var handle = EventWaitHandle.OpenExisting(eventName);
            handle.Set();
            Console.WriteLine($"Flush signaled for PID {pid}");
            return 0;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            Console.Error.WriteLine(
                $"Error: No profiled process found with PID {pid}. " +
                "Ensure the process is running with stats events enabled (method_stats or exception_stats).");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Error: Access denied to flush event for PID {pid}. " +
                "Try running with elevated privileges.");
            return 1;
        }
    }

    private static int FlushMacOS(int pid)
    {
        var semName = $"/MetrejaFlush_{pid}";

        var sem = SemOpen(semName, 0, 0, 0);
        if (sem == SEM_FAILED)
        {
            Console.Error.WriteLine(
                $"Error: No profiled process found with PID {pid}. " +
                "Ensure the process is running with stats events enabled (method_stats or exception_stats).");
            return 1;
        }

        _ = SemPost(sem);
        _ = SemClose(sem);
        Console.WriteLine($"Flush signaled for PID {pid}");
        return 0;
    }

    private static readonly IntPtr SEM_FAILED = new(-1);

    [DllImport("libSystem.B.dylib", EntryPoint = "sem_open", SetLastError = true,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr SemOpen(string name, int oflag, uint mode, uint value);

    [DllImport("libSystem.B.dylib", EntryPoint = "sem_post")]
    private static extern int SemPost(IntPtr sem);

    [DllImport("libSystem.B.dylib", EntryPoint = "sem_close")]
    private static extern int SemClose(IntPtr sem);
}
