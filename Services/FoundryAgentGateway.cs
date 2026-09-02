using System.Diagnostics;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace Label.Agent.Orchestrator.Services;

public sealed class FoundryAgentGateway
{
    private readonly AIProjectClient _projectClient;
    private readonly RunEventPublisher _eventPublisher;

    public FoundryAgentGateway(
        AIProjectClient projectClient,
        RunEventPublisher eventPublisher)
    {
        _projectClient = projectClient;
        _eventPublisher = eventPublisher;
    }

    public async Task<string> InvokeAgentAsync(
        string agentName,
        string request,
        string runId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        var stopwatch = Stopwatch.StartNew();

        await _eventPublisher.PublishAsync(
            connectionId,
            runId,
            "AgentStarted",
            agentName,
            $"Calling {agentName}.",
            cancellationToken: cancellationToken);

        try
        {
            ProjectConversation conversation =
                _projectClient.ProjectOpenAIClient
                    .GetProjectConversationsClient()
                    .CreateProjectConversation();

            ProjectResponsesClient responsesClient =
                _projectClient.ProjectOpenAIClient
                    .GetProjectResponsesClientForAgent(
                        defaultAgent: agentName,
                        defaultConversationId: conversation.Id);

            ResponseResult response =
                responsesClient.CreateResponse(request);

            string output = response.GetOutputText();

            stopwatch.Stop();

            await _eventPublisher.PublishAsync(
                connectionId,
                runId,
                "AgentCompleted",
                agentName,
                $"{agentName} returned a response.",
                status: "Completed",
                durationMilliseconds: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(output)
                ? $"{agentName} returned no text."
                : output;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            await _eventPublisher.PublishAsync(
                connectionId,
                runId,
                "AgentFailed",
                agentName,
                ex.Message,
                status: "Failed",
                durationMilliseconds: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            throw;
        }
    }
}