using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace Label.Agent.Orchestrator.Services;

public sealed class ConversationSessionStore
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public AgentSession? GetSession(string conversationId) =>
        _sessions.TryGetValue(conversationId, out AgentSession? session) ? session : null;

    public void SaveSession(string conversationId, AgentSession session) =>
        _sessions[conversationId] = session;

    public void RemoveSession(string conversationId)
    {
        _sessions.TryRemove(conversationId, out _);
        if (_locks.TryRemove(conversationId, out SemaphoreSlim? gate)) gate.Dispose();
    }

    public SemaphoreSlim GetLock(string conversationId) =>
        _locks.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1));
}
