using System.Diagnostics;
using System.Text.Json;
using Label.Agent.Orchestrator.BeamSearch;
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
    BeamSearchClient beamSearchClient,
    RunEventPublisher events,
    OrchestratorSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<StepResult> ExecuteStepAsync(
        PlannerDecision decision, WorkflowRunState state,
        string connectionId, CancellationToken cancellationToken)
    {
        if (decision.Action != PlannerAction.ExecuteStep || decision.Agent is null)
            throw new InvalidOperationException(
                "Planner decision does not contain an executable step.");
        AgentKind agent = decision.Agent.Value;

        string stepGoal = string.IsNullOrWhiteSpace(decision.StepGoal)
            ? GetDefaultStepGoal(agent)
            : decision.StepGoal;

        string agentRequest = decision.AgentRequest ?? string.Empty;

        if (RequiresPlannerRequest(agent) &&
            string.IsNullOrWhiteSpace(agentRequest))
        {
            throw new InvalidOperationException(
                $"Executable planner decision for {agent} is missing agentRequest.");
        }

        if (!RequiresPlannerRequest(agent) &&
            string.IsNullOrWhiteSpace(agentRequest))
        {
            agentRequest = "{}";
        }

        int stepNumber = state.Results.Count + 1;
        string component = $"Workflow.Step.{stepNumber}";
        await events.PublishAsync(connectionId, state.RunId, "StepStarted",
            component, $"Step {stepNumber}: {stepGoal}",
            cancellationToken: cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            string output = agent switch
            {
                AgentKind.ProductInformation => await gateway.InvokeAgentAsync(
                    settings.ProductAgentName, agentRequest,
                    state.RunId, connectionId, cancellationToken),
                AgentKind.Regulatory => await gateway.InvokeAgentAsync(
                    settings.RegulatoryAgentName, agentRequest,
                    state.RunId, connectionId, cancellationToken),
                AgentKind.TemplateIntelligence => await ExecuteTemplateAsync(
                    agentRequest, state, connectionId, cancellationToken),
                AgentKind.LabelGeneration => await ExecuteGenerationAsync(
                    state, connectionId, cancellationToken),
                AgentKind.CandidateEvaluation => await ExecuteEvaluationAsync(
                    state, connectionId, cancellationToken),
                AgentKind.BeamSearch => await ExecuteBeamSearchAsync(
                    state, connectionId, cancellationToken),
                _ => throw new InvalidOperationException(
                    $"Unsupported component: {agent}")
            };
            stopwatch.Stop();
            var result = new StepResult
            {
                StepNumber = stepNumber,
                Agent = agent,
                Goal = stepGoal,
                Request = agentRequest,
                Output = output,
                Successful = true,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds
            };
            await events.PublishAsync(connectionId, state.RunId,
                "StepCompleted", component, $"Step {stepNumber} completed.",
                "Completed", stopwatch.ElapsedMilliseconds, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await events.PublishAsync(connectionId, state.RunId,
                "StepFailed", component, ex.Message, "Failed",
                stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    private async Task<string> ExecuteTemplateAsync(
        string json, WorkflowRunState state, string connectionId,
        CancellationToken cancellationToken)
    {
        TemplateRecommendationRequest request = ParseJson<TemplateRecommendationRequest>(
            json, "Template Intelligence request");
        await events.PublishAsync(connectionId, state.RunId, "ServiceStarted",
            "TemplateIntelligence", "Evaluating available label templates.",
            cancellationToken: cancellationToken);
        TemplateRecommendationResponse response = await templateClient.RecommendAsync(
            request, cancellationToken);
        await events.PublishAsync(connectionId, state.RunId, "TemplateSelected",
            "TemplateIntelligence",
            $"Selected template '{response.Selected.TemplateName}' with " +
            $"{(int)Math.Round(response.Selected.Confidence * 100)}% confidence.",
            "Completed", cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string> ExecuteGenerationAsync(
        WorkflowRunState state, string connectionId,
        CancellationToken cancellationToken)
    {
        StepResult product = Required(state, AgentKind.ProductInformation);
        StepResult regulatory = Required(state, AgentKind.Regulatory);
        TemplateRecommendationResponse template = ParseJson<TemplateRecommendationResponse>(
            Required(state, AgentKind.TemplateIntelligence).Output,
            "Template Intelligence result");
        var mappedTemplate = new GenerationTemplate(
            template.Template.Id, template.Template.Name,
            template.Template.Sections.OrderBy(item => item.Order)
                .Select(item => new GenerationTemplateSection(
                    item.Key, item.DisplayName, item.Required,
                    item.Order, item.Region, item.Rules)).ToList(),
            template.Template.ContentRules);
        var request = new LabelGenerationRequest(
            state.UserPrompt, product.Output, regulatory.Output,
            mappedTemplate, 3);
        await events.PublishAsync(connectionId, state.RunId,
            "GenerationStarted", "LabelGeneration",
            "Generating three initial label-content candidates.",
            cancellationToken: cancellationToken);
        LabelGenerationResponse response = await generationClient.GenerateAsync(
            request, cancellationToken);
        foreach (LabelCandidate candidate in response.Candidates)
            await events.PublishAsync(connectionId, state.RunId,
                "LabelCandidateGenerated", $"Candidate {candidate.Id}",
                JsonSerializer.Serialize(candidate, JsonOptions), "Completed",
                cancellationToken: cancellationToken);
        await events.PublishAsync(connectionId, state.RunId,
            "GenerationCompleted", "LabelGeneration",
            $"Generated {response.Candidates.Count} initial candidates.",
            "Completed", cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string> ExecuteEvaluationAsync(
        WorkflowRunState state, string connectionId,
        CancellationToken cancellationToken)
    {
        StepResult product = Required(state, AgentKind.ProductInformation);
        StepResult regulatory = Required(state, AgentKind.Regulatory);
        LabelGenerationResponse generation = ParseJson<LabelGenerationResponse>(
            Required(state, AgentKind.LabelGeneration).Output,
            "Label Generation result");
        var request = new CandidateEvaluationRequest(
            state.UserPrompt, product.Output, regulatory.Output,
            generation.TemplateId,
            generation.Candidates.Select(MapEvaluationCandidate).ToList());
        await events.PublishAsync(connectionId, state.RunId,
            "EvaluationStarted", "CandidateEvaluation",
            "Evaluating three initial candidates.",
            cancellationToken: cancellationToken);
        CandidateEvaluationResponse response = await evaluationClient.EvaluateAsync(
            request, cancellationToken);
        foreach (CandidateEvaluation evaluation in response.Evaluations)
            await events.PublishAsync(connectionId, state.RunId,
                "CandidateEvaluated", $"Candidate {evaluation.CandidateId}",
                JsonSerializer.Serialize(evaluation, JsonOptions), "Completed",
                cancellationToken: cancellationToken);
        await events.PublishAsync(connectionId, state.RunId,
            "EvaluationCompleted", "CandidateEvaluation",
            "Initial candidate evaluation completed.", "Completed",
            cancellationToken: cancellationToken);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string> ExecuteBeamSearchAsync(
        WorkflowRunState state, string connectionId,
        CancellationToken cancellationToken)
    {
        StepResult product = Required(state, AgentKind.ProductInformation);
        StepResult regulatory = Required(state, AgentKind.Regulatory);
        TemplateRecommendationResponse template = ParseJson<TemplateRecommendationResponse>(
            Required(state, AgentKind.TemplateIntelligence).Output,
            "Template Intelligence result");
        LabelGenerationResponse generation = ParseJson<LabelGenerationResponse>(
            Required(state, AgentKind.LabelGeneration).Output,
            "Label Generation result");
        CandidateEvaluationResponse evaluation = ParseJson<CandidateEvaluationResponse>(
            Required(state, AgentKind.CandidateEvaluation).Output,
            "Candidate Evaluation result");

        var request = new BeamSearchRequest(
            state.UserPrompt,
            product.Output,
            regulatory.Output,
            new BeamSearchTemplate(
                template.Template.Id, template.Template.Name,
                template.Template.Sections.Select(item => new SearchTemplateSection(
                    item.Key, item.DisplayName, item.Required, item.Order,
                    item.Region, item.Rules)).ToList(),
                template.Template.ContentRules),
            generation.Candidates.Select(MapSearchCandidate).ToList(),
            evaluation.Evaluations.Select(MapSearchEvaluation).ToList(),
            BeamWidth: 2,
            ChildrenPerParent: 2,
            ComplianceThreshold: 50);

        await events.PublishAsync(connectionId, state.RunId,
            "BeamSearchStarted", "BeamSearch",
            "Ranking, pruning, expanding, and re-evaluating candidates.",
            cancellationToken: cancellationToken);
        BeamSearchResponse response = await beamSearchClient.ExecuteAsync(
            request, cancellationToken);
        foreach (SearchNode node in response.Nodes)
            await events.PublishAsync(connectionId, state.RunId,
                "SearchNodeUpdated", $"Candidate {node.CandidateId}",
                JsonSerializer.Serialize(node, JsonOptions), "Completed",
                cancellationToken: cancellationToken);
        await events.PublishAsync(connectionId, state.RunId,
            "BeamSearchCompleted", "BeamSearch",
            JsonSerializer.Serialize(response, JsonOptions), "Completed",
            cancellationToken: cancellationToken);
        if (response.Winner is not null)
        {
            await events.PublishAsync(connectionId, state.RunId,
                "WinnerSelected", "HumanReview",
                $"Candidate {response.Winner.CandidateId} selected for human review. " +
                "No production approval was granted.",
                "Completed", cancellationToken: cancellationToken);
        }
        else
        {
            await events.PublishAsync(connectionId, state.RunId,
                "NoQualifiedWinner", "HumanReview",
                $"No expanded candidate passed the compliance threshold of " +
                $"{response.ComplianceThreshold}. Human review is required.",
                "Completed", cancellationToken: cancellationToken);
        }
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static EvaluationCandidate MapEvaluationCandidate(LabelCandidate c) =>
        new(c.Id, c.Strategy, c.Summary,
            c.Sections.Select(s => new EvaluationSection(
                s.Key, s.DisplayName, s.Content)).ToList(),
            c.Assumptions, c.ReviewFlags);

    private static SearchCandidate MapSearchCandidate(LabelCandidate c) =>
        new(c.Id, c.Strategy, c.Summary,
            c.Sections.Select(s => new SearchSection(
                s.Key, s.DisplayName, s.Content)).ToList(),
            c.Assumptions, c.ReviewFlags);

    private static SearchEvaluation MapSearchEvaluation(CandidateEvaluation e) =>
        new(e.CandidateId, e.Compliance, e.Readability,
            e.BrandAlignment, e.ConsumerClarity, e.OverallScore,
            e.Strengths, e.Risks, e.RationaleSummary);

    private static bool RequiresPlannerRequest(AgentKind agent) =>
        agent is AgentKind.ProductInformation
            or AgentKind.Regulatory
            or AgentKind.TemplateIntelligence;

    private static string GetDefaultStepGoal(AgentKind agent) =>
        agent switch
        {
            AgentKind.ProductInformation =>
                "Retrieve verified product information.",
            AgentKind.Regulatory =>
                "Determine applicable regulatory requirements.",
            AgentKind.TemplateIntelligence =>
                "Select the appropriate label template.",
            AgentKind.LabelGeneration =>
                "Generate initial label candidates.",
            AgentKind.CandidateEvaluation =>
                "Evaluate the initial label candidates.",
            AgentKind.BeamSearch =>
                "Optimize candidates using Beam Search.",
            _ => $"Execute {agent}."
        };

    private static StepResult Required(WorkflowRunState state, AgentKind kind) =>
        state.Results.Where(item => item.Agent == kind && item.Successful)
            .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"A successful {kind} result is required.");

    private static T ParseJson<T>(string value, string description)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"The {description} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The {description} contained invalid JSON.", ex);
        }
    }
}