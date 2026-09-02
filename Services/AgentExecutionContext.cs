namespace Label.Agent.Orchestrator.Services;

public sealed class AgentExecutionContext
{
    private static readonly AsyncLocal<RunContext?> CurrentContext = new();

    public RunContext Current =>
        CurrentContext.Value ??
        throw new InvalidOperationException(
            "No agent execution context is active.");

    public IDisposable Begin(
        string runId,
        string connectionId)
    {
        RunContext? previous = CurrentContext.Value;

        CurrentContext.Value = new RunContext(
            runId,
            connectionId);

        return new ContextScope(() =>
        {
            CurrentContext.Value = previous;
        });
    }

    public sealed record RunContext(
        string RunId,
        string ConnectionId);

    private sealed class ContextScope : IDisposable
    {
        private readonly Action _disposeAction;
        private bool _disposed;

        public ContextScope(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposeAction();
            _disposed = true;
        }
    }
}