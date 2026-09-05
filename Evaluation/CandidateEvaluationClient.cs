using System.Net.Http.Json;

namespace Label.Agent.Orchestrator.Evaluation;

public sealed class CandidateEvaluationClient(HttpClient http)
{
    public async Task<CandidateEvaluationResponse> EvaluateAsync(
        CandidateEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "api/evaluation/candidates", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CandidateEvaluationResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Candidate Evaluation returned an empty response.");
    }
}

public sealed record CandidateEvaluationRequest(
    string UserRequest,
    string ProductInformation,
    string RegulatoryGuidance,
    string TemplateId,
    IReadOnlyList<EvaluationCandidate> Candidates);
public sealed record EvaluationCandidate(
    string Id, string Strategy, string Summary,
    IReadOnlyList<EvaluationSection> Sections,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> ReviewFlags);
public sealed record EvaluationSection(
    string Key, string DisplayName, string Content);
public sealed record CandidateEvaluation(
    string CandidateId, int Compliance, int Readability,
    int BrandAlignment, int ConsumerClarity, int OverallScore,
    IReadOnlyList<string> Strengths, IReadOnlyList<string> Risks,
    string RationaleSummary);
public sealed record EvaluationWeights(
    int Compliance, int Readability, int BrandAlignment, int ConsumerClarity);
public sealed record CandidateEvaluationResponse(
    string EvaluationId,
    IReadOnlyList<CandidateEvaluation> Evaluations,
    EvaluationWeights Weights,
    DateTimeOffset EvaluatedAtUtc,
    string EvaluatorVersion);
