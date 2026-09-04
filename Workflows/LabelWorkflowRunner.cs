using System.Diagnostics;
using Label.Agent.Orchestrator.Configuration;
using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Services;

namespace Label.Agent.Orchestrator.Workflows;

public sealed class LabelWorkflowRunner(
    FoundryAgentGateway gateway,
    RunEventPublisher eventPublisher,
    OrchestratorSettings settings)
{
    public async Task<StepResult> ExecuteStepAsync(
        PlannerDecision decision,
        WorkflowRunState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (decision.Action != PlannerAction.ExecuteStep || decision.Agent is null)
            throw new InvalidOperationException("Planner decision does not contain an executable step.");
        if (string.IsNullOrWhiteSpace(decision.StepGoal) || string.IsNullOrWhiteSpace(decision.AgentRequest))
            throw new InvalidOperationException("Executable planner decision is missing stepGoal or agentRequest.");

        int stepNumber = state.Results.Count + 1;
        string component = $"Workflow.Step.{stepNumber}";
        await eventPublisher.PublishAsync(
            connectionId, state.RunId, "StepStarted", component,
            $"Step {stepNumber}: {decision.StepGoal}", cancellationToken: cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            string agentName = decision.Agent == AgentKind.ProductInformation
                ? settings.ProductAgentName
                : settings.RegulatoryAgentName;

            string output = await gateway.InvokeAgentAsync(
                agentName, decision.AgentRequest, state.RunId,
                connectionId, cancellationToken);

            stopwatch.Stop();
            var result = new StepResult
            {
                StepNumber = stepNumber,
                Agent = decision.Agent.Value,
                Goal = decision.StepGoal,
                Request = decision.AgentRequest,
                Output = output,
                Successful = true,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds
            };

            await eventPublisher.PublishAsync(
                connectionId, state.RunId, "StepCompleted", component,
                $"Step {stepNumber} completed.", "Completed",
                stopwatch.ElapsedMilliseconds, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await eventPublisher.PublishAsync(
                connectionId, state.RunId, "StepFailed", component,
                ex.Message, "Failed", stopwatch.ElapsedMilliseconds,
                cancellationToken);
            throw;
        }
    }
}
