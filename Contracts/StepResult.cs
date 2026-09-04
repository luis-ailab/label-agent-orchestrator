namespace Label.Agent.Orchestrator.Contracts;

public sealed record StepResult
{
    public required int StepNumber { get; init; }

    public required AgentKind Agent { get; init; }

    public required string Goal { get; init; }

    public required string Request { get; init; }

    public required string Output { get; init; }

    public required bool Successful { get; init; }

    public required long DurationMilliseconds { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
        = DateTimeOffset.UtcNow;

    public bool ReusedFromPreviousConversationTurn { get; init; }
}