using System.Runtime.InteropServices;
using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

public class ManualFlushTests
{
    [Fact]
    public async Task ManualFlush_WritesStatsBeforeShutdown()
    {
        var root = TestHelpers.GetSolutionRoot();
        await using var session = await ProfilerRunner.RunInteractiveAsync(
            root, events: ["method_stats"], statsFlushIntervalSeconds: 0);

        // Before signaling flush, NDJSON should have no method_stats
        // (periodic is disabled, process hasn't exited)
        var eventsBefore = await TraceParser.ParseAsync(session.OutputPath);
        Assert.DoesNotContain(eventsBefore, e => e is MethodStatsEvent);

        // Signal manual flush
        await SignalFlushAsync(session.Pid);

        // Poll until stats appear or timeout
        List<MethodStatsEvent> stats;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            await Task.Delay(100);
            var eventsAfter = await TraceParser.ParseAsync(session.OutputPath);
            stats = [.. eventsAfter.OfType<MethodStatsEvent>()];
        } while (stats.Count == 0 && DateTime.UtcNow < deadline);
        Assert.NotEmpty(stats);

        // LoopBody should have exactly 1000 calls
        var loopBody = stats.FirstOrDefault(s => s.M == "LoopBody");
        Assert.NotNull(loopBody);
        Assert.Equal(1000, loopBody.CallCount);

        // Release the process
        await session.ReleaseAndWaitForExitAsync();
    }

    [Fact]
    public async Task ManualFlush_CanFlushMultipleTimes()
    {
        var root = TestHelpers.GetSolutionRoot();
        await using var session = await ProfilerRunner.RunInteractiveAsync(
            root, events: ["method_stats"], statsFlushIntervalSeconds: 0);

        // First flush
        await SignalFlushAsync(session.Pid);
        List<TraceEvent> eventsAfterFirst;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            await Task.Delay(100);
            eventsAfterFirst = await TraceParser.ParseAsync(session.OutputPath);
        } while (!eventsAfterFirst.OfType<MethodStatsEvent>().Any() && DateTime.UtcNow < deadline);

        List<MethodStatsEvent> statsFirst = [.. eventsAfterFirst.OfType<MethodStatsEvent>()];
        Assert.NotEmpty(statsFirst);

        // Second flush — delta stats should be empty (no new calls happened)
        // but the mechanism should not crash
        await SignalFlushAsync(session.Pid);
        await Task.Delay(500);

        // Should still be parseable (no corruption from double flush)
        var eventsAfterSecond = await TraceParser.ParseAsync(session.OutputPath);
        Assert.True(eventsAfterSecond.Count >= eventsAfterFirst.Count);

        await session.ReleaseAndWaitForExitAsync();
    }

    private static async Task SignalFlushAsync(int pid)
    {
        var eventName = $"MetrejaFlush_{pid}";
        const int maxRetries = 10;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var handle = EventWaitHandle.OpenExisting(eventName);
                    handle.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException) when (i < maxRetries - 1)
                {
                    await Task.Delay(200);
                }
            }

            throw new InvalidOperationException($"Failed to open EventWaitHandle '{eventName}' after {maxRetries} retries");
        }
        else
        {
            // macOS/Linux: profiler uses POSIX named semaphore (sem_open with '/' prefix)
            var semName = $"/{eventName}";
            for (var i = 0; i < maxRetries; i++)
            {
                var sem = PosixSemaphore.sem_open(semName, 0, 0, 0);
                if (sem != PosixSemaphore.SEM_FAILED)
                {
                    _ = PosixSemaphore.sem_post(sem);
                    _ = PosixSemaphore.sem_close(sem);
                    return;
                }

                if (i < maxRetries - 1)
                    await Task.Delay(200);
            }

            throw new InvalidOperationException($"Failed to open POSIX semaphore '{semName}' after {maxRetries} retries");
        }
    }

    private static class PosixSemaphore
    {
        public static readonly IntPtr SEM_FAILED = new(-1);

        [DllImport("libSystem.B.dylib", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        public static extern IntPtr sem_open([MarshalAs(UnmanagedType.LPStr)] string name, int oflag, uint mode, uint value);

        [DllImport("libSystem.B.dylib", SetLastError = true)]
        public static extern int sem_post(IntPtr sem);

        [DllImport("libSystem.B.dylib", SetLastError = true)]
        public static extern int sem_close(IntPtr sem);
    }
}
