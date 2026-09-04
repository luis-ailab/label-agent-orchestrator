using System.Text;
using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Services;
using Microsoft.Agents.AI;

namespace Label.Agent.Orchestrator.Planning;

public sealed class PlannerAgentService(
    AIAgent plannerAgent,
    ConversationSessionStore sessionStore,
    PlannerResponseParser parser)
{
    public async Task<PlannerDecision> DecideNextAsync(
        WorkflowRunState state,
        string conversationId,
        CancellationToken cancellationToken)
    {
        AgentSession? session = sessionStore.GetSession(conversationId);
        if (session is null)
        {
            session = await plannerAgent.CreateSessionAsync(cancellationToken);
            sessionStore.SaveSession(conversationId, session);
        }

        AgentResponse response = await plannerAgent.RunAsync(
            message: BuildDecisionRequest(state),
            session: session,
            options: null,
            cancellationToken: cancellationToken);

        return parser.Parse(response.ToString());
    }

    private static string BuildDecisionRequest(WorkflowRunState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Decide the next action for this workflow run.");
        builder.AppendLine($"Original user request: {state.UserPrompt}");
        builder.AppendLine($"Planning iteration: {state.PlanningIteration}");
        builder.AppendLine("Completed workflow steps:");

        if (state.Results.Count == 0)
        {
            builder.AppendLine("None.");
        }
        else
        {
            foreach (StepResult result in state.Results)
            {
                builder.AppendLine($"Step {result.StepNumber}");
                builder.AppendLine($"Agent: {result.Agent}");
                builder.AppendLine($"Goal: {result.Goal}");
                builder.AppendLine($"Successful: {result.Successful}");
                builder.AppendLine("Output:");
                builder.AppendLine(result.Output);
                builder.AppendLine("---");
            }
        }

        builder.AppendLine("Return the required JSON object only.");
        return builder.ToString();
    }
}
