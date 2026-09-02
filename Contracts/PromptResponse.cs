namespace Label.Agent.Orchestrator.Contracts;

public sealed record PromptResponse
{
    public required string RunId { get; init; }

    public required string Response { get; init; }

    public required long DurationMilliseconds { get; init; }

    public required bool Successful { get; init; }
}