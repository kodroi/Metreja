using Metreja.IntegrationTests.Infrastructure;

namespace Metreja.IntegrationTests;

public sealed class ProfilerSessionFixture : IAsyncLifetime
{
    public List<TraceEvent> Events { get; private set; } = [];

    private ProfilerRunner? _runner;

    public async Task InitializeAsync()
    {
        var solutionRoot = ProfilerPrerequisites.FindSolutionRoot();

        var skipReason = ProfilerPrerequisites.GetSkipReason(solutionRoot);
        if (skipReason is not null)
            throw new InvalidOperationException(skipReason);

        var (outputPath, runner) = await ProfilerRunner.RunAsync(solutionRoot);
        _runner = runner;
        Events = await TraceParser.ParseAsync(outputPath);
    }

    public async Task DisposeAsync()
    {
        if (_runner is not null)
            await _runner.DisposeAsync();
    }
}

[CollectionDefinition("ProfilerSession")]
public class ProfilerSessionCollection : ICollectionFixture<ProfilerSessionFixture>;
