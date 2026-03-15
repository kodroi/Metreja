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
    public long? WallTimeNs { get; init; }
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

public record GcEvent : TraceEvent;

public record ContentionEvent : TraceEvent
{
    public required int Tid { get; init; }
}

public record AllocByClassEvent : TraceEvent
{
    public int? Tid { get; init; }
    public required string ClassName { get; init; }
    public required long Count { get; init; }
    public string? AllocAsm { get; init; }
    public string? AllocM { get; init; }
    public string? AllocNs { get; init; }
    public string? AllocCls { get; init; }
}

public record MethodStatsEvent : TraceEvent
{
    public required string Asm { get; init; }
    public required string Ns { get; init; }
    public required string Cls { get; init; }
    public required string M { get; init; }
    public required long CallCount { get; init; }
    public required long TotalSelfNs { get; init; }
    public required long MaxSelfNs { get; init; }
    public required long TotalInclusiveNs { get; init; }
    public required long MaxInclusiveNs { get; init; }
}

public record ExceptionStatsEvent : TraceEvent
{
    public required string ExType { get; init; }
    public required string Asm { get; init; }
    public required string Ns { get; init; }
    public required string Cls { get; init; }
    public required string M { get; init; }
    public required long Count { get; init; }
}
