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

            var allocEvents = events.OfType<AllocByClassEvent>().ToList();
            Assert.NotEmpty(allocEvents);

            // At least some alloc_by_class events should have call-site attribution
            var withCallSite = allocEvents
                .Where(e => !string.IsNullOrEmpty(e.AllocM))
                .ToList();
            Assert.NotEmpty(withCallSite);

            // Call-site attributed events should have non-empty method info
            foreach (var alloc in withCallSite)
            {
                Assert.False(string.IsNullOrEmpty(alloc.AllocM),
                    "allocM should be populated for call-site attributed allocations");
                Assert.True(alloc.Count > 0,
                    "Allocation count should be positive");
            }
        }
    }
}
