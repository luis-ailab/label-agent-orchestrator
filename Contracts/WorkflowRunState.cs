namespace Label.Agent.Orchestrator.Contracts;

public sealed class WorkflowRunState
{
    public required string RunId { get; init; }
    public required string UserPrompt { get; init; }
    public int PlanningIteration { get; set; }
    public List<StepResult> Results { get; } = [];

    public bool HasSuccessfulResult(AgentKind agent) =>
        Results.Any(result => result.Agent == agent && result.Successful);
}
