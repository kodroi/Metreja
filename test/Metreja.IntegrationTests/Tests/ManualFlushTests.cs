using System.Runtime.Versioning;
using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[SupportedOSPlatform("windows")]
public class ManualFlushTests
{
    private static string GetSolutionRoot()
    {
        var root = ProfilerPrerequisites.FindSolutionRoot();
        var skipReason = ProfilerPrerequisites.GetSkipReason(root);
        if (skipReason is not null)
            throw new InvalidOperationException(skipReason);
        return root;
    }

    [Fact]
    public async Task ManualFlush_WritesStatsBeforeShutdown()
    {
        var root = GetSolutionRoot();
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
            stats = eventsAfter.OfType<MethodStatsEvent>().ToList();
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
        var root = GetSolutionRoot();
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

        var statsFirst = eventsAfterFirst.OfType<MethodStatsEvent>().ToList();
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
    }
}
