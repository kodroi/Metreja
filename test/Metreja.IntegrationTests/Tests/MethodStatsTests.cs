using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

public class MethodStatsTests
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
    public async Task MethodStatsOnly_EmitsStatsAndNoEnterLeave()
    {
        var root = GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["method_stats"]);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            // Should have session_metadata
            Assert.Contains(events, e => e is SessionMetadataEvent);

            // Should have method_stats events
            var stats = events.OfType<MethodStatsEvent>().ToList();
            Assert.NotEmpty(stats);

            // Should NOT have enter/leave events
            Assert.DoesNotContain(events, e => e is EnterEvent);
            Assert.DoesNotContain(events, e => e is LeaveEvent);
        }
    }

    [Fact]
    public async Task MethodStats_CallCountMatchesExpected()
    {
        var root = GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["method_stats"]);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);
            var stats = events.OfType<MethodStatsEvent>().ToList();

            // LoopBody is called 1000 times
            var loopBody = stats.FirstOrDefault(s => s.M == "LoopBody");
            Assert.NotNull(loopBody);
            Assert.Equal(1000, loopBody.CallCount);

            // Recurse is called 21 times (depth 20 → 0)
            var recurse = stats.FirstOrDefault(s => s.M == "Recurse");
            Assert.NotNull(recurse);
            Assert.Equal(21, recurse.CallCount);
        }
    }

    [Fact]
    public async Task MethodStats_SelfTimeLessOrEqualInclusive()
    {
        var root = GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["method_stats"]);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);
            var stats = events.OfType<MethodStatsEvent>().ToList();

            foreach (var stat in stats)
            {
                Assert.True(stat.TotalSelfNs <= stat.TotalInclusiveNs,
                    $"{stat.Cls}.{stat.M}: totalSelfNs ({stat.TotalSelfNs}) > totalInclusiveNs ({stat.TotalInclusiveNs})");
                Assert.True(stat.MaxSelfNs <= stat.MaxInclusiveNs,
                    $"{stat.Cls}.{stat.M}: maxSelfNs ({stat.MaxSelfNs}) > maxInclusiveNs ({stat.MaxInclusiveNs})");
            }
        }
    }

    [Fact]
    public async Task ExceptionStatsOnly_EmitsStatsEvents()
    {
        var root = GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["exception_stats"]);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            var exStats = events.OfType<ExceptionStatsEvent>().ToList();
            Assert.NotEmpty(exStats);

            // Should NOT have per-throw exception events
            Assert.DoesNotContain(events, e => e is ExceptionEvent);

            // Should have at least InvalidOperationException and ArgumentException
            var exTypes = exStats.Select(e => e.ExType).ToList();
            Assert.Contains(exTypes, t => t.Contains("InvalidOperationException"));
            Assert.Contains(exTypes, t => t.Contains("ArgumentException"));
        }
    }

    [Fact]
    public async Task EnterLeaveExplicit_BehavesLikeDefault()
    {
        var root = GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["enter", "leave", "exception"]);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            // Should have enter/leave events
            Assert.Contains(events, e => e is EnterEvent);
            Assert.Contains(events, e => e is LeaveEvent);

            // Should NOT have stats events
            Assert.DoesNotContain(events, e => e is MethodStatsEvent);
            Assert.DoesNotContain(events, e => e is ExceptionStatsEvent);

            // LoopBody should have 1000 enter/leave pairs
            var enters = events.Where(e => e is EnterEvent and not LeaveEvent).Cast<EnterEvent>()
                .Count(e => e.M == "LoopBody");
            Assert.Equal(1000, enters);
        }
    }

}
