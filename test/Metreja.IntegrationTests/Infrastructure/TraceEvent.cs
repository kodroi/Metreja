namespace Metreja.IntegrationTests.Infrastructure;

public abstract record TraceEvent
{
    public required string Event { get; init; }
    public required long TsNs { get; init; }
    public required int Pid { get; init; }
    public required string SessionId { get; init; }
}

public record SessionMetadataEvent : TraceEvent
{
    public string Scenario { get; init; } = "";
}

public record EnterEvent : TraceEvent
{
    public required int Tid { get; init; }
    public required int Depth { get; init; }
    public required string Asm { get; init; }
    public required string Ns { get; init; }
    public required string Cls { get; init; }
    public required string M { get; init; }
    public required bool Async { get; init; }
}

public record LeaveEvent : EnterEvent
{
    public required long DeltaNs { get; init; }
    public bool Tailcall { get; init; }
}

public record ExceptionEvent : TraceEvent
{
    public required int Tid { get; init; }
    public required string Asm { get; init; }
    public required string Ns { get; init; }
    public required string Cls { get; init; }
    public required string M { get; init; }
    public required string ExType { get; init; }
}
