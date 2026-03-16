using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class AsyncCallPathTests
{
    private readonly ProfilerSessionFixture _fixture;

    public AsyncCallPathTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public void AsyncCallPath_AllMethodsHaveMatchingEntersAndLeaves()
    {
        var asyncMethods = new HashSet<string>
        {
            "RunAsyncCallPathsAsync", "ComputeAsync", "StepOneAsync", "StepTwoAsync"
        };

        var filtered = _fixture.Events
            .Where(e =>
            {
                if (e is EnterEvent enter) return asyncMethods.Contains(enter.M);
                if (e is LeaveEvent leave) return asyncMethods.Contains(leave.M);
                return false;
            })
            .ToList();

        Assert.NotEmpty(filtered);

        foreach (var method in asyncMethods)
        {
            var enters = filtered.Count(e => e is EnterEvent and not LeaveEvent && ((EnterEvent)e).M == method);
            var leaves = filtered.OfType<LeaveEvent>().Count(e => e.M == method);

            Assert.True(enters > 0, $"Expected at least one enter for {method}");
            Assert.True(enters == leaves,
                $"Enter/leave mismatch for {method}: {enters} enters, {leaves} leaves. " +
                $"Events: [{string.Join(", ", filtered.Where(e => (e is EnterEvent enter && enter.M == method)).Select(e => e is LeaveEvent ? "leave" : "enter"))}]");
        }
    }

    [Fact]
    public void AsyncCallPath_ContainsBothRegularAndStateMachineEvents()
    {
        var asyncMethods = new HashSet<string>
        {
            "RunAsyncCallPathsAsync", "ComputeAsync", "StepOneAsync", "StepTwoAsync"
        };

        var filtered = _fixture.Events
            .Where(e => e is EnterEvent and not LeaveEvent)
            .Cast<EnterEvent>()
            .Where(e => asyncMethods.Contains(e.M))
            .ToList();

        foreach (var method in asyncMethods)
        {
            var methodEvents = filtered.Where(e => e.M == method).ToList();
            Assert.Contains(methodEvents, e => !e.Async); // regular method entry
            Assert.Contains(methodEvents, e => e.Async);   // state machine entry
        }
    }
}
