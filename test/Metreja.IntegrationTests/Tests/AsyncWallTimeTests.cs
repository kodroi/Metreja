using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class AsyncWallTimeTests
{
    private readonly ProfilerSessionFixture _fixture;

    public AsyncWallTimeTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public void AsyncWallTime_LeaveEventContainsWallTimeNs()
    {
        var asyncLeaves = _fixture.Events
            .OfType<LeaveEvent>()
            .Where(e => e.Async)
            .ToList();

        Assert.NotEmpty(asyncLeaves);

        // At least some async leave events should have wallTimeNs populated
        var withWallTime = asyncLeaves.Where(e => e.WallTimeNs.HasValue).ToList();
        Assert.NotEmpty(withWallTime);

        // wallTimeNs should be >= deltaNs (wall time includes await suspension)
        foreach (var leave in withWallTime)
        {
            Assert.True(leave.WallTimeNs!.Value >= leave.DeltaNs,
                $"wallTimeNs should be >= deltaNs, got wallTimeNs={leave.WallTimeNs}, deltaNs={leave.DeltaNs} for {leave.M}");
        }
    }

    [Fact]
    public void AsyncWallTime_NonAsyncMethodsOmitWallTimeNs()
    {
        var syncLeaves = _fixture.Events
            .OfType<LeaveEvent>()
            .Where(e => !e.Async)
            .ToList();

        Assert.NotEmpty(syncLeaves);

        // Sync method leave events should not have wallTimeNs
        foreach (var leave in syncLeaves)
        {
            Assert.Null(leave.WallTimeNs);
        }
    }
}
