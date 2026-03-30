using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

public class GcHeapStatsTests : IAsyncLifetime
{
    private string _solutionRoot = "";
    private string? _skipReason;

    public Task InitializeAsync()
    {
        _solutionRoot = ProfilerPrerequisites.FindSolutionRoot();
        _skipReason = ProfilerPrerequisites.GetSkipReason(_solutionRoot);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GcEnd_ContainsHeapSizeBytes()
    {
        if (_skipReason is not null) return;

        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            _solutionRoot,
            events: ["enter", "leave", "gc_start", "gc_end"]);

        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            var gcEnds = events.OfType<GcEvent>()
                .Where(e => e.Event == "gc_end")
                .ToList();

            // TestApp forces blocking GC via GC.Collect — gc_end events are expected.
            Assert.NotEmpty(gcEnds);
            foreach (var ev in gcEnds)
            {
                Assert.True(ev.DurationNs > 0, "gc_end should have durationNs > 0");
                Assert.True(ev.HeapSizeBytes > 0, "gc_end should have heapSizeBytes > 0");
            }
        }
    }

    [Fact]
    public async Task GcHeapStats_EmittedWhenEnabled()
    {
        if (_skipReason is not null) return;

        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            _solutionRoot,
            events: ["enter", "leave", "gc_start", "gc_end", "gc_heap_stats"]);

        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            var heapStats = events.OfType<GcHeapStatsEvent>().ToList();

            // GcHeapStats requires EventPipe (ICorProfilerInfo12 / .NET 5+).
            // TestApp forces blocking GC, so events are expected on supported runtimes.
            Assert.NotEmpty(heapStats);
            foreach (var ev in heapStats)
            {
                Assert.True(ev.TsNs > 0, "gc_heap_stats should have a valid timestamp");
                Assert.True(ev.Gen0SizeBytes >= 0, "gen0SizeBytes should be non-negative");
                Assert.True(ev.Gen1SizeBytes >= 0, "gen1SizeBytes should be non-negative");
                Assert.True(ev.Gen2SizeBytes >= 0, "gen2SizeBytes should be non-negative");
                Assert.True(ev.LohSizeBytes >= 0, "lohSizeBytes should be non-negative");
            }
        }
    }

    [Fact]
    public async Task GcHeapStats_NotEmittedWhenDisabled()
    {
        if (_skipReason is not null) return;

        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            _solutionRoot,
            events: ["enter", "leave", "gc_start", "gc_end"]);

        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            var heapStats = events.OfType<GcHeapStatsEvent>().ToList();
            Assert.Empty(heapStats);
        }
    }
}
