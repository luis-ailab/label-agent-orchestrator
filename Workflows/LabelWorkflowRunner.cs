using System.Diagnostics;
using System.Text.Json;
using Label.Agent.Orchestrator.Configuration;
using Label.Agent.Orchestrator.Contracts;
using Label.Agent.Orchestrator.LabelGeneration;
using Label.Agent.Orchestrator.Services;
using Label.Agent.Orchestrator.TemplateIntelligence;

namespace Label.Agent.Orchestrator.Workflows;

public sealed class LabelWorkflowRunner(
    FoundryAgentGateway gateway,
    TemplateIntelligenceClient templateClient,
    LabelGenerationClient generationClient,
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

        await events.PublishAsync(
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
                    await gateway.InvokeAgentAsync(
                        settings.ProductAgentName,
                        decision.AgentRequest,
                        state.RunId,
                        connectionId,
                        cancellationToken),

                AgentKind.Regulatory =>
                    await gateway.InvokeAgentAsync(
                        settings.RegulatoryAgentName,
                        decision.AgentRequest,
                        state.RunId,
                        connectionId,
                        cancellationToken),

                AgentKind.TemplateIntelligence =>
                    await ExecuteTemplateAsync(
                        decision.AgentRequest,
                        state,
                        connectionId,
                        cancellationToken),

                AgentKind.LabelGeneration =>
                    await ExecuteGenerationAsync(
                        state,
                        connectionId,
                        cancellationToken),

                _ => throw new InvalidOperationException(
                    $"Unsupported component: {decision.Agent.Value}")
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

            await events.PublishAsync(
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

            await events.PublishAsync(
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

    private async Task<string> ExecuteTemplateAsync(
        string json,
        WorkflowRunState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        TemplateRecommendationRequest request =
            ParseJson<TemplateRecommendationRequest>(
                json,
                "Template Intelligence request");

        await events.PublishAsync(
            connectionId,
            state.RunId,
            "ServiceStarted",
            "TemplateIntelligence",
            "Evaluating available label templates.",
            cancellationToken: cancellationToken);

        TemplateRecommendationResponse response =
            await templateClient.RecommendAsync(request, cancellationToken);

        int confidencePercentage =
            (int)Math.Round(response.Selected.Confidence * 100);

        await events.PublishAsync(
            connectionId,
            state.RunId,
            "TemplateSelected",
            "TemplateIntelligence",
            $"Selected template '{response.Selected.TemplateName}' " +
            $"with {confidencePercentage}% confidence.",
            "Completed",
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string> ExecuteGenerationAsync(
        WorkflowRunState state,
        string connectionId,
        CancellationToken cancellationToken)
    {
        StepResult productResult = GetLatestRequiredResult(
            state,
            AgentKind.ProductInformation,
            "Product Information");

        StepResult regulatoryResult = GetLatestRequiredResult(
            state,
            AgentKind.Regulatory,
            "Regulatory");

        StepResult templateResult = GetLatestRequiredResult(
            state,
            AgentKind.TemplateIntelligence,
            "Template Intelligence");

        TemplateRecommendationResponse templateRecommendation =
            ParseJson<TemplateRecommendationResponse>(
                templateResult.Output,
                "Template Intelligence workflow result");

        GenerationTemplate generationTemplate = MapTemplate(
            templateRecommendation.Template);

        var request = new LabelGenerationRequest(
            UserRequest: state.UserPrompt,
            ProductInformation: productResult.Output,
            RegulatoryGuidance: regulatoryResult.Output,
            Template: generationTemplate,
            CandidateCount: 3);

        await events.PublishAsync(
            connectionId,
            state.RunId,
            "GenerationStarted",
            "LabelGeneration",
            "Generating three label-content candidates from verified workflow results.",
            cancellationToken: cancellationToken);

        LabelGenerationResponse response =
            await generationClient.GenerateAsync(request, cancellationToken);

        foreach (LabelCandidate candidate in response.Candidates)
        {
            await events.PublishAsync(
                connectionId,
                state.RunId,
                "LabelCandidateGenerated",
                $"Candidate {candidate.Id}",
                JsonSerializer.Serialize(candidate, JsonOptions),
                "Completed",
                cancellationToken: cancellationToken);
        }

        await events.PublishAsync(
            connectionId,
            state.RunId,
            "GenerationCompleted",
            "LabelGeneration",
            $"Generated {response.Candidates.Count} candidates using " +
            $"template '{response.TemplateId}'.",
            "Completed",
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static StepResult GetLatestRequiredResult(
        WorkflowRunState state,
        AgentKind agent,
        string displayName)
    {
        StepResult? result = state.Results
            .Where(item => item.Agent == agent && item.Successful)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault();

        return result ?? throw new InvalidOperationException(
            $"Label Generation requires a successful {displayName} result.");
    }

    private static GenerationTemplate MapTemplate(
        LabelTemplate source)
    {
        var sections = source.Sections
            .OrderBy(section => section.Order)
            .Select(section => new GenerationTemplateSection(
                Key: section.Key,
                DisplayName: section.DisplayName,
                Required: section.Required,
                Order: section.Order,
                Region: section.Region,
                Rules: section.Rules))
            .ToList();

        return new GenerationTemplate(
            Id: source.Id,
            Name: source.Name,
            Sections: sections,
            ContentRules: source.ContentRules);
    }

    private static T ParseJson<T>(
        string value,
        string description)
    {
        string json = StripMarkdownFences(value);

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"The {description} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The {description} contained invalid JSON.",
                ex);
        }
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
