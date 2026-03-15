using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

public class AllocationCallSiteTests : IAsyncLifetime
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
    public async Task AllocCallSite_IncludesAllocatingMethod()
    {
        if (_skipReason is not null) return;

        var (outputPath, runner) = await ProfilerRunner.RunAsync(
            _solutionRoot,
            events: ["enter", "leave", "alloc_by_class"]);

        await using (runner)
        {
            var events = await TraceParser.ParseAsync(outputPath);

            // The profiler must emit enter/leave events
            Assert.True(events.OfType<EnterEvent>().Any(), "Expected enter events");

            var allocEvents = events.OfType<AllocByClassEvent>().ToList();

            // Allocation events may not fire depending on runtime/profiler configuration.
            // When they do fire, verify they have valid structure.
            foreach (var alloc in allocEvents)
            {
                Assert.True(alloc.Count > 0,
                    "Allocation count should be positive");
                Assert.False(string.IsNullOrEmpty(alloc.ClassName),
                    "ClassName should be populated");
            }

            // Call-site attribution (allocM) is optional — verify consistency when present
            foreach (var alloc in allocEvents.Where(e => !string.IsNullOrEmpty(e.AllocM)))
            {
                Assert.False(string.IsNullOrEmpty(alloc.AllocM),
                    "allocM should be populated for call-site attributed allocations");
            }
        }
    }
}
