using System.Diagnostics;
using System.Text.Json;
using Label.Agent.Orchestrator.Configuration;
using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.Evaluation;
using Label.Agent.Orchestrator.LabelGeneration;
using Label.Agent.Orchestrator.Services;
using Label.Agent.Orchestrator.TemplateIntelligence;

namespace Label.Agent.Orchestrator.Workflows;

public sealed class LabelWorkflowRunner(
    FoundryAgentGateway gateway,
    TemplateIntelligenceClient templateClient,
    LabelGenerationClient generationClient,
    CandidateEvaluationClient evaluationClient,
    RunEventPublisher events,
    OrchestratorSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<StepResult> ExecuteStepAsync(
        PlannerDecision decision,
        WorkflowRunState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (decision.Action != PlannerAction.ExecuteStep || decision.Agent is null)
            throw new InvalidOperationException("Planner decision does not contain an executable step.");
        if (string.IsNullOrWhiteSpace(decision.StepGoal) || string.IsNullOrWhiteSpace(decision.AgentRequest))
            throw new InvalidOperationException("Executable planner decision is missing stepGoal or agentRequest.");

        int stepNumber = state.Results.Count + 1;
        string component = $"Workflow.Step.{stepNumber}";
        await events.PublishAsync(connectionId, state.RunId, "StepStarted", component,
            $"Step {stepNumber}: {decision.StepGoal}", cancellationToken: cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            string output = decision.Agent.Value switch
            {
                AgentKind.ProductInformation => await gateway.InvokeAgentAsync(
                    settings.ProductAgentName, decision.AgentRequest, state.RunId, connectionId, cancellationToken),
                AgentKind.Regulatory => await gateway.InvokeAgentAsync(
                    settings.RegulatoryAgentName, decision.AgentRequest, state.RunId, connectionId, cancellationToken),
                AgentKind.TemplateIntelligence => await ExecuteTemplateAsync(
                    decision.AgentRequest, state, connectionId, cancellationToken),
                AgentKind.LabelGeneration => await ExecuteGenerationAsync(
                    state, connectionId, cancellationToken),
                AgentKind.CandidateEvaluation => await ExecuteEvaluationAsync(
                    state, connectionId, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported component: {decision.Agent.Value}")
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
            await events.PublishAsync(connectionId, state.RunId, "StepCompleted", component,
                $"Step {stepNumber} completed.", "Completed", stopwatch.ElapsedMilliseconds, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await events.PublishAsync(connectionId, state.RunId, "StepFailed", component,
                ex.Message, "Failed", stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    private async Task<string> ExecuteTemplateAsync(
        string json, WorkflowRunState state, string connectionId, CancellationToken cancellationToken)
    {
        TemplateRecommendationRequest request = ParseJson<TemplateRecommendationRequest>(
            json, "Template Intelligence request");
        await events.PublishAsync(connectionId, state.RunId, "ServiceStarted", "TemplateIntelligence",
            "Evaluating available label templates.", cancellationToken: cancellationToken);
        TemplateRecommendationResponse response = await templateClient.RecommendAsync(request, cancellationToken);
        int confidence = (int)Math.Round(response.Selected.Confidence * 100);
        await events.PublishAsync(connectionId, state.RunId, "TemplateSelected", "TemplateIntelligence",
            $"Selected template '{response.Selected.TemplateName}' with {confidence}% confidence.",
            "Completed", cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string> ExecuteGenerationAsync(
        WorkflowRunState state, string connectionId, CancellationToken cancellationToken)
    {
        StepResult product = GetLatestRequiredResult(state, AgentKind.ProductInformation, "Product Information");
        StepResult regulatory = GetLatestRequiredResult(state, AgentKind.Regulatory, "Regulatory");
        StepResult template = GetLatestRequiredResult(state, AgentKind.TemplateIntelligence, "Template Intelligence");
        TemplateRecommendationResponse templateResponse = ParseJson<TemplateRecommendationResponse>(
            template.Output, "Template Intelligence workflow result");
        var generationTemplate = new GenerationTemplate(
            templateResponse.Template.Id,
            templateResponse.Template.Name,
            templateResponse.Template.Sections.OrderBy(item => item.Order)
                .Select(item => new GenerationTemplateSection(
                    item.Key, item.DisplayName, item.Required, item.Order, item.Region, item.Rules)).ToList(),
            templateResponse.Template.ContentRules);
        var request = new LabelGenerationRequest(
            state.UserPrompt, product.Output, regulatory.Output, generationTemplate, 3);

        await events.PublishAsync(connectionId, state.RunId, "GenerationStarted", "LabelGeneration",
            "Generating three label-content candidates from verified workflow results.",
            cancellationToken: cancellationToken);
        LabelGenerationResponse response = await generationClient.GenerateAsync(request, cancellationToken);
        foreach (LabelCandidate candidate in response.Candidates)
            await events.PublishAsync(connectionId, state.RunId, "LabelCandidateGenerated",
                $"Candidate {candidate.Id}", JsonSerializer.Serialize(candidate, JsonOptions),
                "Completed", cancellationToken: cancellationToken);
        await events.PublishAsync(connectionId, state.RunId, "GenerationCompleted", "LabelGeneration",
            $"Generated {response.Candidates.Count} candidates using template '{response.TemplateId}'.",
            "Completed", cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string> ExecuteEvaluationAsync(
        WorkflowRunState state, string connectionId, CancellationToken cancellationToken)
    {
        StepResult product = GetLatestRequiredResult(state, AgentKind.ProductInformation, "Product Information");
        StepResult regulatory = GetLatestRequiredResult(state, AgentKind.Regulatory, "Regulatory");
        StepResult generation = GetLatestRequiredResult(state, AgentKind.LabelGeneration, "Label Generation");
        LabelGenerationResponse generationResponse = ParseJson<LabelGenerationResponse>(
            generation.Output, "Label Generation workflow result");

        var request = new CandidateEvaluationRequest(
            state.UserPrompt,
            product.Output,
            regulatory.Output,
            generationResponse.TemplateId,
            generationResponse.Candidates.Select(candidate => new EvaluationCandidate(
                candidate.Id, candidate.Strategy, candidate.Summary,
                candidate.Sections.Select(section => new EvaluationSection(
                    section.Key, section.DisplayName, section.Content)).ToList(),
                candidate.Assumptions, candidate.ReviewFlags)).ToList());

        await events.PublishAsync(connectionId, state.RunId, "EvaluationStarted", "CandidateEvaluation",
            "Evaluating three label candidates using the Phase 3 scoring rubric.",
            cancellationToken: cancellationToken);
        CandidateEvaluationResponse response = await evaluationClient.EvaluateAsync(request, cancellationToken);
        foreach (CandidateEvaluation evaluation in response.Evaluations)
            await events.PublishAsync(connectionId, state.RunId, "CandidateEvaluated",
                $"Candidate {evaluation.CandidateId}", JsonSerializer.Serialize(evaluation, JsonOptions),
                "Completed", cancellationToken: cancellationToken);
        await events.PublishAsync(connectionId, state.RunId, "EvaluationCompleted", "CandidateEvaluation",
            $"Evaluated {response.Evaluations.Count} candidates. No winner was selected.",
            "Completed", cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static StepResult GetLatestRequiredResult(
        WorkflowRunState state, AgentKind agent, string displayName)
    {
        return state.Results.Where(item => item.Agent == agent && item.Successful)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"The workflow requires a successful {displayName} result.");
    }

    private static T ParseJson<T>(string value, string description)
    {
        string json = StripMarkdownFences(value);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"The {description} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The {description} contained invalid JSON.", ex);
        }
    }

    private static string StripMarkdownFences(string value)
    {
        string trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        int firstLineEnd = trimmed.IndexOf('\n');
        int finalFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && finalFence > firstLineEnd
            ? trimmed[(firstLineEnd + 1)..finalFence].Trim()
            : trimmed;
    }
}
