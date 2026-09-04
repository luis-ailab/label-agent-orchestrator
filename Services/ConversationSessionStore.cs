using System.Collections.Concurrent;
using Label.Agent.Orchestrator.Contracts;
using Microsoft.Agents.AI;

namespace Label.Agent.Orchestrator.Services;

public sealed class ConversationSessionStore
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private readonly ConcurrentDictionary<string, List<StepResult>>
        _workflowMemory = new();

    public AgentSession? GetSession(string conversationId) =>
        _sessions.TryGetValue(conversationId, out AgentSession? session)
            ? session
            : null;

    public void SaveSession(
        string conversationId,
        AgentSession session)
    {
        _sessions[conversationId] = session;
    }

    public IReadOnlyList<StepResult> GetRecentResults(
        string conversationId)
    {
        if (!_workflowMemory.TryGetValue(
                conversationId,
                out List<StepResult>? results))
        {
            return [];
        }

        return results
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .ToList();
    }

    public void SaveResult(
        string conversationId,
        StepResult result)
    {
        List<StepResult> results =
            _workflowMemory.GetOrAdd(
                conversationId,
                _ => []);

        lock (results)
        {
            results.Add(result);

            if (results.Count > 10)
            {
                results.RemoveRange(
                    0,
                    results.Count - 10);
            }
        }
    }

    public void RemoveSession(string conversationId)
    {
        _sessions.TryRemove(conversationId, out _);

        _workflowMemory.TryRemove(
            conversationId,
            out _);

        if (_locks.TryRemove(
                conversationId,
                out SemaphoreSlim? gate))
        {
            gate.Dispose();
        }
    }

    public SemaphoreSlim GetLock(string conversationId) =>
        _locks.GetOrAdd(
            conversationId,
            _ => new SemaphoreSlim(1, 1));
}