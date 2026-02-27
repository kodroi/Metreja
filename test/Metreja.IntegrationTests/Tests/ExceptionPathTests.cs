using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class ExceptionPathTests
{
    private readonly ProfilerSessionFixture _fixture;

    public ExceptionPathTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public Task ExceptionPath_MatchesSnapshot()
    {
        var exceptionMethods = new HashSet<string>
        {
            "RunExceptionPaths", "ThrowingMethod", "NestedExceptionMethod", "WrapperMethod"
        };

        var filtered = _fixture.Events
            .Where(e =>
            {
                if (e is EnterEvent enter) return exceptionMethods.Contains(enter.M);
                if (e is LeaveEvent leave) return exceptionMethods.Contains(leave.M);
                if (e is ExceptionEvent ex) return exceptionMethods.Contains(ex.M);
                return false;
            })
            .ToList();

        var normalized = TraceNormalizer.Normalize(filtered, collapseLoops: false);
        return Verifier.Verify(normalized);
    }
}
