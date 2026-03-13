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

    /// <summary>
    /// Regression test for exception-unwind double-pop bug.
    /// When method_stats + exception_stats are both enabled, self-catch exceptions
    /// (caught in the same method that throws) previously caused the call stack to
    /// misalign, producing wildly incorrect inclusive times (parent = 0, child inflated).
    /// </summary>
    [Fact]
    public async Task MethodStatsWithExceptionStats_SelfCatchDoesNotCorruptCallStack()
    {
        var root = GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["method_stats", "exception_stats"]);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);
            var stats = events.OfType<MethodStatsEvent>().ToList();

            // SelfCatchParent calls SelfCatchChild which throws and catches internally.
            // Before the fix, SelfCatchParent would show 0 inclusive time because
            // ExceptionUnwindFunctionEnter double-popped SelfCatchChild's stack entry.
            var parent = stats.FirstOrDefault(s => s.M == "SelfCatchParent");
            var child = stats.FirstOrDefault(s => s.M == "SelfCatchChild");

            Assert.NotNull(parent);
            Assert.NotNull(child);

            // Parent must have non-zero inclusive time (was 0 before fix)
            Assert.True(parent.TotalInclusiveNs > 0,
                $"SelfCatchParent.TotalInclusiveNs should be > 0 but was {parent.TotalInclusiveNs}");

            // Child's inclusive time must not exceed parent's (stack misalignment signature)
            Assert.True(child.TotalInclusiveNs <= parent.TotalInclusiveNs,
                $"SelfCatchChild.TotalInclusiveNs ({child.TotalInclusiveNs}) should not exceed " +
                $"SelfCatchParent.TotalInclusiveNs ({parent.TotalInclusiveNs})");

            // Self time <= inclusive time invariant must still hold
            Assert.True(parent.TotalSelfNs <= parent.TotalInclusiveNs,
                $"SelfCatchParent: totalSelfNs ({parent.TotalSelfNs}) > totalInclusiveNs ({parent.TotalInclusiveNs})");
            Assert.True(child.TotalSelfNs <= child.TotalInclusiveNs,
                $"SelfCatchChild: totalSelfNs ({child.TotalSelfNs}) > totalInclusiveNs ({child.TotalInclusiveNs})");
        }
    }

    /// <summary>
    /// Regression test for recursive exception-catch frame identification.
    /// RecursiveCatch(3) → RecursiveCatch(2) → RecursiveCatch(1) → RecursiveCatch(0) throws.
    /// RecursiveCatch(1) catches. The unwind must pop depth=0's frame even though it has
    /// the same FunctionID as the catcher (depth=1). Uses stack depth to distinguish.
    /// </summary>
    [Fact]
    public async Task MethodStatsWithExceptionStats_RecursiveCatchPreservesCorrectFrame()
    {
        var root = GetSolutionRoot();
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            root, events: ["method_stats", "exception_stats"]);
        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);
            var stats = events.OfType<MethodStatsEvent>().ToList();

            var recursiveCatch = stats.FirstOrDefault(s => s.M == "RecursiveCatch");
            var outer = stats.FirstOrDefault(s => s.M == "RecursiveCatchOuter");

            Assert.NotNull(recursiveCatch);
            Assert.NotNull(outer);

            // RecursiveCatch is called 4 times (depth 3,2,1,0)
            Assert.Equal(4, recursiveCatch.CallCount);

            // Outer must have non-zero inclusive time
            Assert.True(outer.TotalInclusiveNs > 0,
                $"RecursiveCatchOuter.TotalInclusiveNs should be > 0 but was {outer.TotalInclusiveNs}");

            // RecursiveCatch inclusive must not exceed outer (stack misalignment signature)
            // Allow 1ms tolerance for timer jitter across platforms
            const long toleranceNs = 1_000_000;
            Assert.True(recursiveCatch.TotalInclusiveNs <= outer.TotalInclusiveNs + toleranceNs,
                $"RecursiveCatch.TotalInclusiveNs ({recursiveCatch.TotalInclusiveNs}) should not exceed " +
                $"RecursiveCatchOuter.TotalInclusiveNs ({outer.TotalInclusiveNs}) by more than {toleranceNs}ns");

            // Self time <= inclusive time invariant (with same tolerance)
            Assert.True(recursiveCatch.TotalSelfNs <= recursiveCatch.TotalInclusiveNs + toleranceNs,
                $"RecursiveCatch: totalSelfNs ({recursiveCatch.TotalSelfNs}) > totalInclusiveNs ({recursiveCatch.TotalInclusiveNs})");
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
