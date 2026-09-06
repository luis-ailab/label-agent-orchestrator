using System.Net.Http.Json;

namespace Label.Agent.Orchestrator.BeamSearch;

public sealed class BeamSearchClient(HttpClient http)
{
    public async Task<BeamSearchResponse> ExecuteAsync(
        BeamSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "api/beam-search/execute", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BeamSearchResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Beam Search returned an empty response.");
    }
}

public sealed record BeamSearchRequest(
    string UserRequest, string ProductInformation,
    string RegulatoryGuidance, BeamSearchTemplate Template,
    IReadOnlyList<SearchCandidate> InitialCandidates,
    IReadOnlyList<SearchEvaluation> InitialEvaluations,
    int BeamWidth = 2, int ChildrenPerParent = 2,
    int ComplianceThreshold = 70);
public sealed record BeamSearchTemplate(
    string Id, string Name,
    IReadOnlyList<SearchTemplateSection> Sections,
    IReadOnlyList<string> ContentRules);
public sealed record SearchTemplateSection(
    string Key, string DisplayName, bool Required, int Order,
    string Region, IReadOnlyList<string> Rules);
public sealed record SearchCandidate(
    string Id, string Strategy, string Summary,
    IReadOnlyList<SearchSection> Sections,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> ReviewFlags);
public sealed record SearchSection(string Key, string DisplayName, string Content);
public sealed record SearchEvaluation(
    string CandidateId, int Compliance, int Readability,
    int BrandAlignment, int ConsumerClarity, int OverallScore,
    IReadOnlyList<string> Strengths, IReadOnlyList<string> Risks,
    string RationaleSummary);
public sealed record SearchNode(
    string CandidateId, string? ParentCandidateId, int Depth,
    string Status, SearchCandidate Candidate,
    SearchEvaluation Evaluation, string DecisionReason);
public sealed record BeamSearchResponse(
    string SearchId, int BeamWidth, int ChildrenPerParent,
    int ComplianceThreshold, IReadOnlyList<SearchNode> Nodes,
    SearchNode? Winner, string Outcome,
    IReadOnlyList<string> AuditTrail,
    DateTimeOffset CompletedAtUtc, string AlgorithmVersion,
    bool HumanApprovalRequired);
