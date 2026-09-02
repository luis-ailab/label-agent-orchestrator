using Microsoft.Agents.AI;

namespace Label.Agent.Orchestrator.Services;

public sealed class ConversationSessionStore
{
    private readonly Dictionary<string, AgentSession>
        _sessions = new();

    public AgentSession? GetSession(
        string connectionId)
    {
        _sessions.TryGetValue(
            connectionId,
            out AgentSession? session);

        return session;
    }

    public void SaveSession(
        string connectionId,
        AgentSession session)
    {
        _sessions[connectionId] = session;
    }

    public void RemoveSession(
        string connectionId)
    {
        _sessions.Remove(connectionId);
    }
}