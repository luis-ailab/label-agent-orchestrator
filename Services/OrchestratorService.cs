using System.Diagnostics;
using Label.Agent.Orchestrator.Configuration;
using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Planning;
using Label.Agent.Orchestrator.Workflows;

namespace Label.Agent.Orchestrator.Services;

public sealed class OrchestratorService(
    PlannerAgentService planner,
    LabelWorkflowRunner workflow,
    RunEventPublisher eventPublisher,
    ConversationSessionStore sessionStore,
    OrchestratorSettings settings,
    ILogger<OrchestratorService> logger)
{
    public async Task<PromptResponse> RunAsync(
        string prompt,
        string conversationId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        string runId = $"run-{Guid.NewGuid():N}";
        var stopwatch = Stopwatch.StartNew();
        SemaphoreSlim conversationLock = sessionStore.GetLock(conversationId);
        await conversationLock.WaitAsync(cancellationToken);

        try
        {
            await eventPublisher.PublishAsync(
                connectionId, runId, "RunStarted", "WebApplication",
                "Prompt received.", cancellationToken: cancellationToken);

            var state = new WorkflowRunState
            {
                RunId = runId,
                UserPrompt = prompt
            };

            await eventPublisher.PublishAsync(
                connectionId, runId, "WorkflowStarted", "LabelWorkflow",
                "Planner and workflow execution started.", cancellationToken: cancellationToken);

            for (int iteration = 1; iteration <= settings.MaxPlanningIterations; iteration++)
            {
                state.PlanningIteration = iteration;
                string planningEvent = iteration == 1 ? "PlanningStarted" : "PlannerEvaluationStarted";
                await eventPublisher.PublishAsync(
                    connectionId, runId, planningEvent, "PlannerAgent",
                    iteration == 1
                        ? "Creating the initial execution decision."
                        : "Evaluating workflow results and deciding the next action.",
                    cancellationToken: cancellationToken);

                PlannerDecision decision = await planner.DecideNextAsync(
                    state, conversationId, cancellationToken);

                await eventPublisher.PublishAsync(
                    connectionId, runId, "PlannerDecisionCreated", "PlannerAgent",
                    BuildSafeDecisionSummary(decision), "Completed",
                    cancellationToken: cancellationToken);

                switch (decision.Action)
                {
                    case PlannerAction.ExecuteStep:
                        StepResult result = await workflow.ExecuteStepAsync(
                            decision, state, connectionId, cancellationToken);
                        state.Results.Add(result);
                        break;

                    case PlannerAction.Clarify:
                        stopwatch.Stop();
                        string question = decision.ClarificationQuestion
                            ?? "Please provide the missing information needed to continue.";
                        await eventPublisher.PublishAsync(
                            connectionId, runId, "ClarificationRequired", "PlannerAgent",
                            question, "Completed", stopwatch.ElapsedMilliseconds,
                            cancellationToken);
                        return Success(runId, question, stopwatch.ElapsedMilliseconds);

                    case PlannerAction.Complete:
                        stopwatch.Stop();
                        string answer = decision.FinalAnswer
                            ?? "The workflow completed, but the planner did not return a final answer.";
                        await eventPublisher.PublishAsync(
                            connectionId, runId, "FinalAnswerCreated", "PlannerAgent",
                            "Final answer created from workflow results.", "Completed",
                            stopwatch.ElapsedMilliseconds, cancellationToken);
                        await eventPublisher.PublishAsync(
                            connectionId, runId, "WorkflowCompleted", "LabelWorkflow",
                            "Workflow completed successfully.", "Completed",
                            stopwatch.ElapsedMilliseconds, cancellationToken);
                        await eventPublisher.PublishAsync(
                            connectionId, runId, "RunCompleted", "WebApplication",
                            "Run completed successfully.", "Completed",
                            stopwatch.ElapsedMilliseconds, cancellationToken);
                        return Success(runId, answer, stopwatch.ElapsedMilliseconds);
                }
            }

            stopwatch.Stop();
            const string limitMessage =
                "The workflow reached its planning-iteration limit before producing a final answer. Review the execution trace or refine the request.";
            await eventPublisher.PublishAsync(
                connectionId, runId, "WorkflowLimitReached", "LabelWorkflow",
                limitMessage, "Failed", stopwatch.ElapsedMilliseconds, cancellationToken);
            return Failure(runId, limitMessage, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Planner workflow run {RunId} failed.", runId);
            await eventPublisher.PublishAsync(
                connectionId, runId, "RunFailed", "WebApplication",
                ex.Message, "Failed", stopwatch.ElapsedMilliseconds,
                CancellationToken.None);
            return Failure(runId, "The request could not be completed.", stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            conversationLock.Release();
        }
    }

    private static string BuildSafeDecisionSummary(PlannerDecision decision) =>
        decision.Action switch
        {
            PlannerAction.ExecuteStep =>
                $"Next action: execute {decision.Agent}. Goal: {decision.StepGoal}",
            PlannerAction.Complete => "Next action: produce the final answer.",
            PlannerAction.Clarify => "Next action: request clarification from the user.",
            _ => "Planner returned a decision."
        };

    private static PromptResponse Success(string runId, string response, long duration) =>
        new() { RunId = runId, Response = response, DurationMilliseconds = duration, Successful = true };

    private static PromptResponse Failure(string runId, string response, long duration) =>
        new() { RunId = runId, Response = response, DurationMilliseconds = duration, Successful = false };
}
