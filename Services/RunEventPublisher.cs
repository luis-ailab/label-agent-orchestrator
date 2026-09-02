using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Label.Agent.Orchestrator.Services;

public sealed class RunEventPublisher
{
    private readonly IHubContext<OrchestratorHub> _hubContext;

    public RunEventPublisher(
        IHubContext<OrchestratorHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishAsync(
        string connectionId,
        string runId,
        string eventType,
        string component,
        string message,
        string status = "Running",
        long? durationMilliseconds = null,
        CancellationToken cancellationToken = default)
    {
        var runEvent = new AgentRunEvent
        {
            RunId = runId,
            EventId = Guid.NewGuid().ToString("N"),
            EventType = eventType,
            Component = component,
            Message = message,
            Status = status,
            DurationMilliseconds = durationMilliseconds
        };

        return _hubContext.Clients
            .Client(connectionId)
            .SendAsync(
                "AgentRunEvent",
                runEvent,
                cancellationToken);
    }
}