namespace Label.Agent.Orchestrator.Contracts;

public sealed record PlannerDecision
{
    public required PlannerAction Action { get; init; }
    public AgentKind? Agent { get; init; }
    public string? StepGoal { get; init; }
    public string? AgentRequest { get; init; }
    public string? FinalAnswer { get; init; }
    public string? ClarificationQuestion { get; init; }
    public string? RationaleSummary { get; init; }
}
