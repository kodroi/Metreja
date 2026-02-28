using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class FullTraceSnapshotTests
{
    private readonly ProfilerSessionFixture _fixture;

    public FullTraceSnapshotTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public void FullTrace_ContainsAllScenarios()
    {
        var methods = _fixture.Events
            .OfType<EnterEvent>()
            .Select(e => e.M)
            .ToHashSet();

        // Sync call path
        Assert.Contains("RunSyncCallPaths", methods);
        Assert.Contains("OuterMethod", methods);
        Assert.Contains("MiddleMethod", methods);
        Assert.Contains("InnerMethod", methods);

        // Async call path
        Assert.Contains("RunAsyncCallPathsAsync", methods);
        Assert.Contains("ComputeAsync", methods);
        Assert.Contains("StepOneAsync", methods);
        Assert.Contains("StepTwoAsync", methods);

        // Exception path
        Assert.Contains("RunExceptionPaths", methods);
        Assert.Contains("ThrowingMethod", methods);

        // Tight loop
        Assert.Contains("RunTightLoop", methods);
        Assert.Contains("LoopBody", methods);

        // Deep recursion
        Assert.Contains("RunDeepRecursion", methods);
        Assert.Contains("Recurse", methods);
    }

    [Fact]
    public void FullTrace_HasExpectedEventCount()
    {
        // 1000 LoopBody enter+leave alone = 2000 events, plus all other scenarios
        Assert.True(_fixture.Events.Count > 2050,
            $"Expected more than 2050 total events, got {_fixture.Events.Count}");
    }

    [Fact]
    public void FullTrace_EventTypesAreAllExpected()
    {
        foreach (var evt in _fixture.Events)
        {
            Assert.True(
                evt is SessionMetadataEvent or EnterEvent or LeaveEvent or ExceptionEvent,
                $"Unexpected event type: {evt.GetType().Name}");
        }
    }
}
