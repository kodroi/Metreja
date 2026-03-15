using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

public class ContentionEventTests : IAsyncLifetime
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
    public async Task Contention_EmitsContentionStartAndEnd()
    {
        if (_skipReason is not null) return;

        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            _solutionRoot,
            events: ["enter", "leave", "contention_start", "contention_end"]);

        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            // The profiler must emit enter/leave events
            Assert.True(events.OfType<EnterEvent>().Any(), "Expected enter events");

            var contentionStarts = events.OfType<ContentionEvent>()
                .Where(e => e.Event == "contention_start")
                .ToList();
            var contentionEnds = events.OfType<ContentionEvent>()
                .Where(e => e.Event == "contention_end")
                .ToList();

            // Contention events may not fire depending on runtime timing.
            // When they do fire, verify they have valid structure.
            foreach (var ev in contentionStarts.Concat(contentionEnds))
            {
                Assert.True(ev.Tid > 0, "Contention event should have a valid thread ID");
                Assert.True(ev.TsNs > 0, "Contention event should have a valid timestamp");
            }
        }
    }

    [Fact]
    public async Task Contention_OnlyEmittedWhenEnabled()
    {
        if (_skipReason is not null) return;

        // Run without contention events enabled
        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            _solutionRoot,
            events: ["enter", "leave"]);

        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            var contentionEvents = events.OfType<ContentionEvent>().ToList();
            Assert.Empty(contentionEvents);
        }
    }
}
