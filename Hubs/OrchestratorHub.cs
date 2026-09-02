using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Services;
using Microsoft.AspNetCore.SignalR;

namespace Label.Agent.Orchestrator.Hubs;

public sealed class OrchestratorHub : Hub
{
    private readonly OrchestratorService _orchestratorService;
    private readonly ConversationSessionStore _sessionStore;
    private readonly ILogger<OrchestratorHub> _logger;

    public OrchestratorHub(
        OrchestratorService orchestratorService,
        ConversationSessionStore sessionStore,
        ILogger<OrchestratorHub> logger)
    {
        _orchestratorService = orchestratorService;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task<PromptResponse> RunPrompt(string prompt)
    {
        _logger.LogInformation(
            "RunPrompt invoked. Connection: {ConnectionId}",
            Context.ConnectionId);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new HubException("A prompt is required.");
        }

        try
        {
            return await _orchestratorService.RunAsync(
                prompt,
                Context.ConnectionId,
                Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "RunPrompt failed. Connection: {ConnectionId}",
                Context.ConnectionId);

            throw new HubException(
                $"Orchestrator execution failed: {ex.Message}");
        }
    }

    public Task ResetConversation()
    {
        _sessionStore.RemoveSession(Context.ConnectionId);

        _logger.LogInformation(
            "Conversation reset. Connection: {ConnectionId}",
            Context.ConnectionId);

        return Task.CompletedTask;
    }
}