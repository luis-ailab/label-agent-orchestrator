using System.Net.Http.Json;

namespace Label.Agent.Orchestrator.TemplateIntelligence;

public sealed class TemplateIntelligenceClient(HttpClient http)
{
    public async Task<TemplateRecommendationResponse> RecommendAsync(
        TemplateRecommendationRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/templates/recommend", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TemplateRecommendationResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Template Intelligence returned an empty response.");
    }
}

public sealed record TemplateRecommendationRequest(string Market, string ProductCategory,
    string DosageForm, string PackageType, IReadOnlyList<string>? RegulatoryRequiredSections = null,
    IReadOnlyList<string>? Tags = null);
public sealed record TemplateCandidate(string TemplateId, string TemplateName, double Score,
    double Confidence, IReadOnlyList<string> Reasons, IReadOnlyList<string> MissingRequiredSections);
public sealed record TemplateSection(string Key, string DisplayName, bool Required, int Order,
    string Region, IReadOnlyList<string> Rules);
public sealed record LabelTemplate(string Id, string Name, int Version, string Status,
    IReadOnlyList<string> Markets, IReadOnlyList<string> ProductCategories,
    IReadOnlyList<string> DosageForms, IReadOnlyList<string> PackageTypes,
    IReadOnlyList<TemplateSection> Sections, IReadOnlyList<string> ContentRules,
    IReadOnlyList<string> Tags);
public sealed record TemplateRecommendationResponse(TemplateCandidate Selected, LabelTemplate Template,
    IReadOnlyList<TemplateCandidate> Candidates, DateTimeOffset EvaluatedAtUtc, string AlgorithmVersion);