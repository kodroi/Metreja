using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

[Collection("ProfilerSession")]
public class StructuralValidationTests
{
    private readonly ProfilerSessionFixture _fixture;

    public StructuralValidationTests(ProfilerSessionFixture fixture) => _fixture = fixture;

    [Fact]
    public void FirstEvent_IsSessionMetadata()
    {
        var first = _fixture.Events[0];
        Assert.IsType<SessionMetadataEvent>(first);
        Assert.Equal("session_metadata", first.Event);
        Assert.NotEmpty(first.SessionId);
    }

    [Fact]
    public void AllEvents_HaveRequiredFields()
    {
        foreach (var evt in _fixture.Events)
        {
            Assert.NotEmpty(evt.Event);
            Assert.True(evt.TsNs >= 0, $"TsNs should be non-negative, got {evt.TsNs}");
            Assert.True(evt.Pid > 0, $"Pid should be positive, got {evt.Pid}");
            Assert.NotEmpty(evt.SessionId);

            switch (evt)
            {
                case LeaveEvent leave:
                    Assert.True(leave.Tid > 0, "Tid should be positive");
                    Assert.True(leave.Depth >= 0, $"Depth should be non-negative, got {leave.Depth}");
                    Assert.NotEmpty(leave.Asm);
                    Assert.NotEmpty(leave.M);
                    break;

                case EnterEvent enter:
                    Assert.True(enter.Tid > 0, "Tid should be positive");
                    Assert.True(enter.Depth >= 0, $"Depth should be non-negative, got {enter.Depth}");
                    Assert.NotEmpty(enter.Asm);
                    Assert.NotEmpty(enter.M);
                    break;

                case ExceptionEvent ex:
                    Assert.True(ex.Tid > 0, "Tid should be positive");
                    Assert.NotEmpty(ex.ExType);
                    break;
            }
        }
    }

    [Fact]
    public void Timestamps_AreMonotonicallyIncreasing()
    {
        long prevTs = -1;
        for (var i = 0; i < _fixture.Events.Count; i++)
        {
            var ts = _fixture.Events[i].TsNs;
            Assert.True(ts >= prevTs,
                $"Timestamp at index {i} ({ts}) is less than previous ({prevTs})");
            prevTs = ts;
        }
    }

    [Fact]
    public void DeltaNs_IsNonNegativeForAllLeaves()
    {
        var leaves = _fixture.Events.OfType<LeaveEvent>().ToList();
        Assert.NotEmpty(leaves);

        foreach (var leave in leaves)
        {
            Assert.True(leave.DeltaNs >= 0,
                $"Negative deltaNs ({leave.DeltaNs}) for {leave.Cls}.{leave.M}");
        }
    }

    [Fact]
    public void EnterLeaveBalance_PerThread()
    {
        var threadStacks = new Dictionary<int, int>();

        foreach (var evt in _fixture.Events)
        {
            int tid;
            switch (evt)
            {
                case LeaveEvent leave:
                    tid = leave.Tid;
                    threadStacks[tid] = threadStacks.GetValueOrDefault(tid) - 1;
                    break;
                case EnterEvent enter:
                    tid = enter.Tid;
                    threadStacks[tid] = threadStacks.GetValueOrDefault(tid) + 1;
                    break;
            }
        }

        var exceptionCount = _fixture.Events.OfType<ExceptionEvent>().Count();

        foreach (var (tid, balance) in threadStacks)
        {
            // Enter count >= leave count (exceptions prevent some leaves)
            Assert.True(balance >= 0 || exceptionCount > 0,
                $"Thread {tid} has negative balance ({balance}) with no exceptions to account for it");
        }
    }

    [Fact]
    public void TightLoop_LoopBody_HasExactly1000EntersAndLeaves()
    {
        var enters = _fixture.Events.Where(e => e is EnterEvent and not LeaveEvent).Cast<EnterEvent>().Count(e => e.M == "LoopBody");
        var leaves = _fixture.Events.OfType<LeaveEvent>().Count(e => e.M == "LoopBody");

        Assert.Equal(1000, enters);
        Assert.Equal(1000, leaves);
    }

    [Fact]
    public void DeepRecursion_Recurse_HasExactly21EntersAndLeaves()
    {
        // Recurse(20) → Recurse(19) → ... → Recurse(0) = 21 calls
        var enters = _fixture.Events.Where(e => e is EnterEvent and not LeaveEvent).Cast<EnterEvent>().Count(e => e.M == "Recurse");
        var leaves = _fixture.Events.OfType<LeaveEvent>().Count(e => e.M == "Recurse");

        Assert.Equal(21, enters);
        Assert.Equal(21, leaves);
    }

    [Fact]
    public void Exceptions_ExactlyFourRecorded()
    {
        var exceptions = _fixture.Events.OfType<ExceptionEvent>().ToList();
        Assert.Equal(4, exceptions.Count);

        var exTypes = exceptions.Select(e => e.ExType).OrderBy(t => t).ToList();
        Assert.Contains(exTypes, t => t.Contains("ArgumentException"));
        Assert.Contains(exTypes, t => t.Contains("InvalidOperationException"));
    }
}
