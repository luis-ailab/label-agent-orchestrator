using System.Diagnostics;
using Label.Agent.Orchestrator.Contracts;
using Microsoft.Agents.AI;

namespace Label.Agent.Orchestrator.Services;

public sealed class OrchestratorService
{
    private readonly AIAgent _orchestrator;
    private readonly RunEventPublisher _eventPublisher;
    private readonly AgentExecutionContext _executionContext;
    private readonly ConversationSessionStore _sessionStore;

    public OrchestratorService(
        AIAgent orchestrator,
        RunEventPublisher eventPublisher,
        AgentExecutionContext executionContext,
        ConversationSessionStore sessionStore)
    {
        _orchestrator = orchestrator;
        _eventPublisher = eventPublisher;
        _executionContext = executionContext;
        _sessionStore = sessionStore;
    }

    public async Task<PromptResponse> RunAsync(
        string prompt,
        string connectionId,
        CancellationToken cancellationToken)
    {
        string runId = $"run-{Guid.NewGuid():N}";
        var stopwatch = Stopwatch.StartNew();

        using IDisposable scope =
            _executionContext.Begin(runId, connectionId);

        await _eventPublisher.PublishAsync(
            connectionId,
            runId,
            "RunStarted",
            "WebApplication",
            "Prompt received.",
            cancellationToken: cancellationToken);

        await _eventPublisher.PublishAsync(
            connectionId,
            runId,
            "OrchestratorStarted",
            "LabelPlatformOrchestrator",
            "Analyzing the request and selecting agents.",
            cancellationToken: cancellationToken);

        try
        {
            AgentSession? session =
                _sessionStore.GetSession(
                    connectionId);

            if (session is null)
            {
                session =
                    await _orchestrator.CreateSessionAsync(
                        cancellationToken);

                _sessionStore.SaveSession(
                    connectionId,
                    session);
            }

            AgentResponse agentResponse =
                await _orchestrator.RunAsync(
                    message: prompt,
                    session: session,
                    options: null,
                    cancellationToken: cancellationToken);

            stopwatch.Stop();

            await _eventPublisher.PublishAsync(
                connectionId,
                runId,
                "OrchestratorCompleted",
                "LabelPlatformOrchestrator",
                "Final response created.",
                status: "Completed",
                durationMilliseconds: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            await _eventPublisher.PublishAsync(
                connectionId,
                runId,
                "RunCompleted",
                "WebApplication",
                "Run completed successfully.",
                status: "Completed",
                durationMilliseconds: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            return new PromptResponse
            {
                RunId = runId,
                Response = agentResponse.ToString(),
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                Successful = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            await _eventPublisher.PublishAsync(
                connectionId,
                runId,
                "RunFailed",
                "WebApplication",
                ex.Message,
                status: "Failed",
                durationMilliseconds: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            return new PromptResponse
            {
                RunId = runId,
                Response = "The request could not be completed.",
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                Successful = false
            };
        }
    }
}