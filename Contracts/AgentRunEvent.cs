namespace Label.Agent.Orchestrator.Contracts;

public sealed record AgentRunEvent
{
    public required string RunId { get; init; }
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required string Component { get; init; }
    public required string Message { get; init; }
    public string Status { get; init; } = "Running";
    public long? DurationMilliseconds { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
