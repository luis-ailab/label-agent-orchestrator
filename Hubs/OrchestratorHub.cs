using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Services;
using Microsoft.AspNetCore.SignalR;

namespace Label.Agent.Orchestrator.Hubs;

public sealed class OrchestratorHub(
    OrchestratorService orchestratorService,
    ConversationSessionStore sessionStore,
    ILogger<OrchestratorHub> logger) : Hub
{
    public async Task<PromptResponse> RunPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new HubException("A prompt is required.");

        try
        {
            return await orchestratorService.RunAsync(
                prompt,
                Context.ConnectionId,
                Context.ConnectionId,
                Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RunPrompt failed for {ConnectionId}.", Context.ConnectionId);
            throw new HubException($"Orchestrator execution failed: {ex.Message}");
        }
    }

    public Task ResetConversation()
    {
        sessionStore.RemoveSession(Context.ConnectionId);
        logger.LogInformation("Conversation reset for {ConnectionId}.", Context.ConnectionId);
        return Task.CompletedTask;
    }
}
