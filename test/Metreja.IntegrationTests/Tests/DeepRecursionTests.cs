using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class DeepRecursionTests
{
    private readonly ProfilerSessionFixture _fixture;

    public DeepRecursionTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public Task DeepRecursion_MatchesSnapshot()
    {
        var recursionMethods = new HashSet<string> { "RunDeepRecursion", "Recurse" };

        var filtered = _fixture.Events
            .Where(e =>
            {
                if (e is EnterEvent enter) return recursionMethods.Contains(enter.M);
                if (e is LeaveEvent leave) return recursionMethods.Contains(leave.M);
                return false;
            })
            .ToList();

        var normalized = TraceNormalizer.Normalize(filtered, collapseLoops: false);
        return Verifier.Verify(normalized);
    }
}
