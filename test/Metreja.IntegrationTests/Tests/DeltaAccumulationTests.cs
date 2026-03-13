using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests.Tests;

/// <summary>
/// Tests that multiple method_stats events for the same method (as emitted by
/// periodic flush deltas) are correctly parsed and can be summed by consumers.
/// </summary>
public class DeltaAccumulationTests : IAsyncDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "metreja-delta-test-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task TraceParser_ParsesMultipleMethodStatsForSameMethod()
    {
        Directory.CreateDirectory(_tempDir);
        var ndjsonPath = Path.Combine(_tempDir, "delta-test.ndjson");

        // Simulate two periodic flush deltas + final flush for the same method
        var lines = new[]
        {
            """{"event":"session_metadata","tsNs":1000,"pid":1234,"sessionId":"test01","scenario":"delta-test"}""",
            """{"event":"method_stats","tsNs":0,"pid":1234,"sessionId":"test01","asm":"TestApp","ns":"TestApp","cls":"Foo","m":"Bar","callCount":100,"totalSelfNs":5000,"maxSelfNs":200,"totalInclusiveNs":8000,"maxInclusiveNs":300}""",
            """{"event":"method_stats","tsNs":0,"pid":1234,"sessionId":"test01","asm":"TestApp","ns":"TestApp","cls":"Foo","m":"Bar","callCount":150,"totalSelfNs":7500,"maxSelfNs":250,"totalInclusiveNs":12000,"maxInclusiveNs":350}""",
            """{"event":"method_stats","tsNs":0,"pid":1234,"sessionId":"test01","asm":"TestApp","ns":"TestApp","cls":"Foo","m":"Bar","callCount":50,"totalSelfNs":2500,"maxSelfNs":180,"totalInclusiveNs":4000,"maxInclusiveNs":280}"""
        };
        await File.WriteAllLinesAsync(ndjsonPath, lines);

        var events = await TraceParser.ParseAsync(ndjsonPath);
        var stats = events.OfType<MethodStatsEvent>().Where(s => s.M == "Bar").ToList();

        // Three separate method_stats events should be parsed
        Assert.Equal(3, stats.Count);

        // Consumers (HotspotsAnalyzer, DiffAnalyzer) sum with +=, so verify totals accumulate
        var totalCalls = stats.Sum(s => s.CallCount);
        var totalSelfNs = stats.Sum(s => s.TotalSelfNs);
        var totalInclusiveNs = stats.Sum(s => s.TotalInclusiveNs);
        var maxSelfNs = stats.Max(s => s.MaxSelfNs);
        var maxInclusiveNs = stats.Max(s => s.MaxInclusiveNs);

        Assert.Equal(300, totalCalls);         // 100 + 150 + 50
        Assert.Equal(15000, totalSelfNs);      // 5000 + 7500 + 2500
        Assert.Equal(24000, totalInclusiveNs); // 8000 + 12000 + 4000
        Assert.Equal(250, maxSelfNs);          // max(200, 250, 180)
        Assert.Equal(350, maxInclusiveNs);     // max(300, 350, 280)
    }

    [Fact]
    public async Task TraceParser_DeltaMethodStats_MixedWithOtherEvents()
    {
        Directory.CreateDirectory(_tempDir);
        var ndjsonPath = Path.Combine(_tempDir, "delta-mixed-test.ndjson");

        // Simulate periodic flush interleaved with other event types
        var lines = new[]
        {
            """{"event":"session_metadata","tsNs":1000,"pid":1234,"sessionId":"test01","scenario":"mixed-test"}""",
            """{"event":"method_stats","tsNs":0,"pid":1234,"sessionId":"test01","asm":"TestApp","ns":"TestApp","cls":"Svc","m":"Run","callCount":10,"totalSelfNs":1000,"maxSelfNs":200,"totalInclusiveNs":2000,"maxInclusiveNs":400}""",
            """{"event":"exception_stats","tsNs":0,"pid":1234,"sessionId":"test01","exType":"System.Exception","asm":"TestApp","ns":"TestApp","cls":"Svc","m":"Run","count":2}""",
            """{"event":"method_stats","tsNs":0,"pid":1234,"sessionId":"test01","asm":"TestApp","ns":"TestApp","cls":"Svc","m":"Run","callCount":20,"totalSelfNs":3000,"maxSelfNs":500,"totalInclusiveNs":5000,"maxInclusiveNs":800}"""
        };
        await File.WriteAllLinesAsync(ndjsonPath, lines);

        var events = await TraceParser.ParseAsync(ndjsonPath);

        // Should parse all event types correctly
        Assert.Single(events.OfType<SessionMetadataEvent>());
        Assert.Equal(2, events.OfType<MethodStatsEvent>().Count());
        Assert.Single(events.OfType<ExceptionStatsEvent>());

        // Accumulate method stats
        var methodStats = events.OfType<MethodStatsEvent>().ToList();
        Assert.Equal(30, methodStats.Sum(s => s.CallCount)); // 10 + 20
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }

        return ValueTask.CompletedTask;
    }
}
