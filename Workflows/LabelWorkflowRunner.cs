using System.Diagnostics;
using System.Text.Json;
using Label.Agent.Orchestrator.Configuration;
using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Services;
using Label.Agent.Orchestrator.TemplateIntelligence;

namespace Label.Agent.Orchestrator.Workflows;

public sealed class LabelWorkflowRunner(
    FoundryAgentGateway gateway,
    TemplateIntelligenceClient templateIntelligenceClient,
    RunEventPublisher eventPublisher,
    OrchestratorSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<StepResult> ExecuteStepAsync(
        PlannerDecision decision,
        WorkflowRunState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (decision.Action != PlannerAction.ExecuteStep ||
            decision.Agent is null)
        {
            throw new InvalidOperationException(
                "Planner decision does not contain an executable step.");
        }

        if (string.IsNullOrWhiteSpace(decision.StepGoal) ||
            string.IsNullOrWhiteSpace(decision.AgentRequest))
        {
            throw new InvalidOperationException(
                "Executable planner decision is missing stepGoal or agentRequest.");
        }

        int stepNumber = state.Results.Count + 1;
        string component = $"Workflow.Step.{stepNumber}";

        await eventPublisher.PublishAsync(
            connectionId,
            state.RunId,
            "StepStarted",
            component,
            $"Step {stepNumber}: {decision.StepGoal}",
            cancellationToken: cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            string output = decision.Agent.Value switch
            {
                AgentKind.ProductInformation =>
                    await ExecuteFoundryAgentAsync(
                        settings.ProductAgentName,
                        decision.AgentRequest,
                        state,
                        connectionId,
                        cancellationToken),

                AgentKind.Regulatory =>
                    await ExecuteFoundryAgentAsync(
                        settings.RegulatoryAgentName,
                        decision.AgentRequest,
                        state,
                        connectionId,
                        cancellationToken),

                AgentKind.TemplateIntelligence =>
                    await ExecuteTemplateIntelligenceAsync(
                        decision.AgentRequest,
                        state,
                        connectionId,
                        cancellationToken),

                _ => throw new InvalidOperationException(
                    $"Unsupported workflow component: {decision.Agent.Value}.")
            };

            stopwatch.Stop();

            var result = new StepResult
            {
                StepNumber = stepNumber,
                Agent = decision.Agent.Value,
                Goal = decision.StepGoal,
                Request = decision.AgentRequest,
                Output = output,
                Successful = true,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds
            };

            await eventPublisher.PublishAsync(
                connectionId,
                state.RunId,
                "StepCompleted",
                component,
                $"Step {stepNumber} completed.",
                "Completed",
                stopwatch.ElapsedMilliseconds,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            await eventPublisher.PublishAsync(
                connectionId,
                state.RunId,
                "StepFailed",
                component,
                ex.Message,
                "Failed",
                stopwatch.ElapsedMilliseconds,
                cancellationToken);

            throw;
        }
    }

    private async Task<string> ExecuteFoundryAgentAsync(
        string agentName,
        string request,
        WorkflowRunState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        return await gateway.InvokeAgentAsync(
            agentName,
            request,
            state.RunId,
            connectionId,
            cancellationToken);
    }

    private async Task<string> ExecuteTemplateIntelligenceAsync(
        string request,
        WorkflowRunState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        TemplateRecommendationRequest recommendationRequest =
            ParseTemplateRequest(request);

        await eventPublisher.PublishAsync(
            connectionId,
            state.RunId,
            "ServiceStarted",
            "TemplateIntelligence",
            "Evaluating available label templates.",
            cancellationToken: cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            TemplateRecommendationResponse recommendation =
                await templateIntelligenceClient.RecommendAsync(
                    recommendationRequest,
                    cancellationToken);

            stopwatch.Stop();

            await eventPublisher.PublishAsync(
                connectionId,
                state.RunId,
                "TemplateSelected",
                "TemplateIntelligence",
                BuildTemplateSelectionMessage(recommendation),
                "Completed",
                stopwatch.ElapsedMilliseconds,
                cancellationToken);

            return JsonSerializer.Serialize(
                recommendation,
                JsonOptions);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            await eventPublisher.PublishAsync(
                connectionId,
                state.RunId,
                "ServiceFailed",
                "TemplateIntelligence",
                ex.Message,
                "Failed",
                stopwatch.ElapsedMilliseconds,
                cancellationToken);

            throw;
        }
    }

    private static TemplateRecommendationRequest ParseTemplateRequest(
        string request)
    {
        string json = StripMarkdownFences(request);

        try
        {
            TemplateRecommendationRequest? parsed =
                JsonSerializer.Deserialize<TemplateRecommendationRequest>(
                    json,
                    JsonOptions);

            if (parsed is null)
            {
                throw new InvalidOperationException(
                    "The Template Intelligence request was empty.");
            }

            ValidateTemplateRequest(parsed);

            return parsed;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The planner returned an invalid Template Intelligence request. " +
                "A valid JSON object is required.",
                ex);
        }
    }

    private static void ValidateTemplateRequest(
        TemplateRecommendationRequest request)
    {
        var missingFields = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Market))
        {
            missingFields.Add("market");
        }

        if (string.IsNullOrWhiteSpace(request.ProductCategory))
        {
            missingFields.Add("productCategory");
        }

        if (string.IsNullOrWhiteSpace(request.DosageForm))
        {
            missingFields.Add("dosageForm");
        }

        if (string.IsNullOrWhiteSpace(request.PackageType))
        {
            missingFields.Add("packageType");
        }

        if (missingFields.Count > 0)
        {
            throw new InvalidOperationException(
                "Template Intelligence requires these fields: " +
                string.Join(", ", missingFields));
        }
    }

    private static string BuildTemplateSelectionMessage(
        TemplateRecommendationResponse recommendation)
    {
        int confidencePercentage =
            (int)Math.Round(recommendation.Selected.Confidence * 100);

        return
            $"Selected template '{recommendation.Selected.TemplateName}' " +
            $"with {confidencePercentage}% confidence.";
    }

    private static string StripMarkdownFences(string value)
    {
        string trimmed = value.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstLineEnd = trimmed.IndexOf('\n');
        int finalFence = trimmed.LastIndexOf(
            "```",
            StringComparison.Ordinal);

        if (firstLineEnd < 0 || finalFence <= firstLineEnd)
        {
            return trimmed;
        }

        return trimmed[(firstLineEnd + 1)..finalFence].Trim();
    }
}