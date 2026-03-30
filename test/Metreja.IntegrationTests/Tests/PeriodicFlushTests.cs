using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

public class PeriodicFlushTests
{
    [Fact]
    public async Task MethodStats_WithFlushDisabled_StillEmitsStatsAtShutdown()
    {
        var root = TestHelpers.GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["method_stats"], statsFlushIntervalSeconds: 0);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            // Should still have method_stats from the final flush at shutdown
            var stats = events.OfType<MethodStatsEvent>().ToList();
            Assert.NotEmpty(stats);

            // LoopBody should have exactly 1000 calls
            var loopBody = stats.FirstOrDefault(s => s.M == "LoopBody");
            Assert.NotNull(loopBody);
            Assert.Equal(1000, loopBody.CallCount);

            // Should NOT have enter/leave events
            Assert.DoesNotContain(events, e => e is EnterEvent);
            Assert.DoesNotContain(events, e => e is LeaveEvent);
        }
    }

    [Fact]
    public async Task MethodStats_WithFlushEnabled_StillProducesCorrectTotals()
    {
        var root = TestHelpers.GetSolutionRoot();
        // Use default interval (30s) — TestApp finishes in < 1s so periodic flush
        // won't fire, but this validates the code path doesn't break anything
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["method_stats"], statsFlushIntervalSeconds: 30);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            var stats = events.OfType<MethodStatsEvent>().ToList();
            Assert.NotEmpty(stats);

            // LoopBody still has exactly 1000 calls
            var loopBody = stats.FirstOrDefault(s => s.M == "LoopBody");
            Assert.NotNull(loopBody);
            Assert.Equal(1000, loopBody.CallCount);

            // Self time <= inclusive time invariant must hold
            foreach (var stat in stats)
            {
                Assert.True(stat.TotalSelfNs <= stat.TotalInclusiveNs,
                    $"{stat.Cls}.{stat.M}: totalSelfNs ({stat.TotalSelfNs}) > totalInclusiveNs ({stat.TotalInclusiveNs})");
            }
        }
    }

    [Fact]
    public async Task ExceptionStats_WithFlushDisabled_StillEmitsStatsAtShutdown()
    {
        var root = TestHelpers.GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["exception_stats"], statsFlushIntervalSeconds: 0);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            var exStats = events.OfType<ExceptionStatsEvent>().ToList();
            Assert.NotEmpty(exStats);

            var exTypes = exStats.Select(e => e.ExType).ToList();
            Assert.Contains(exTypes, t => t.Contains("InvalidOperationException"));
            Assert.Contains(exTypes, t => t.Contains("ArgumentException"));
        }
    }
}
