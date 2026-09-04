using System.Text.Json;
using System.Text.Json.Serialization;
using Label.Agent.Orchestrator.Contracts;

namespace Label.Agent.Orchestrator.Planning;

public sealed class PlannerResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PlannerDecision Parse(string response)
    {
        string json = StripMarkdownFences(response);
        PlannerDecision? decision = JsonSerializer.Deserialize<PlannerDecision>(json, Options);
        return decision ?? throw new InvalidOperationException("Planner returned an empty decision.");
    }

    private static string StripMarkdownFences(string value)
    {
        string trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        int firstLineEnd = trimmed.IndexOf('\n');
        int finalFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || finalFence <= firstLineEnd) return trimmed;
        return trimmed[(firstLineEnd + 1)..finalFence].Trim();
    }
}
