using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class SyncCallPathTests
{
    private readonly ProfilerSessionFixture _fixture;

    public SyncCallPathTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public Task SyncCallPath_MatchesSnapshot()
    {
        var syncMethods = new HashSet<string>
        {
            "RunSyncCallPaths", "OuterMethod", "MiddleMethod", "InnerMethod"
        };

        var filtered = _fixture.Events
            .Where(e => e is EnterEvent or LeaveEvent)
            .Where(e =>
            {
                var m = e is EnterEvent enter ? enter.M : ((LeaveEvent)e).M;
                return syncMethods.Contains(m);
            })
            .ToList();

        var normalized = TraceNormalizer.Normalize(filtered, collapseLoops: false);
        return Verifier.Verify(normalized);
    }
}
