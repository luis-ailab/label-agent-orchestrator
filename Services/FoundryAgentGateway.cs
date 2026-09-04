using System.Diagnostics;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace Label.Agent.Orchestrator.Services;

public sealed class FoundryAgentGateway(
    AIProjectClient projectClient,
    RunEventPublisher eventPublisher)
{
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
        await eventPublisher.PublishAsync(
            connectionId, runId, "AgentStarted", agentName,
            $"Calling {agentName}.", cancellationToken: cancellationToken);

        try
        {
            ProjectConversation conversation = projectClient.ProjectOpenAIClient
                .GetProjectConversationsClient()
                .CreateProjectConversation();

            ProjectResponsesClient responsesClient = projectClient.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(
                    defaultAgent: agentName,
                    defaultConversationId: conversation.Id);

            ResponseResult response = responsesClient.CreateResponse(request);
            string output = response.GetOutputText();
            stopwatch.Stop();

            await eventPublisher.PublishAsync(
                connectionId, runId, "AgentCompleted", agentName,
                $"{agentName} returned a response.", "Completed",
                stopwatch.ElapsedMilliseconds, cancellationToken);

            return string.IsNullOrWhiteSpace(output)
                ? $"{agentName} returned no text."
                : output;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await eventPublisher.PublishAsync(
                connectionId, runId, "AgentFailed", agentName,
                ex.Message, "Failed", stopwatch.ElapsedMilliseconds,
                cancellationToken);
            throw;
        }
    }
}
