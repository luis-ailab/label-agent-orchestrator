namespace Label.Agent.Orchestrator.Configuration;

public sealed class OrchestratorSettings
{
    public string ProjectEndpoint { get; init; } = string.Empty;
    public string ModelDeploymentName { get; init; } = string.Empty;
    public string ProductAgentName { get; init; } = string.Empty;
    public string RegulatoryAgentName { get; init; } = string.Empty;
    public int MaxPlanningIterations { get; init; } = 4;

    public static OrchestratorSettings Load(IConfiguration configuration)
    {
        var settings = new OrchestratorSettings
        {
            ProjectEndpoint = configuration["FoundryProjectEndpoint"] ?? string.Empty,
            ModelDeploymentName = configuration["ModelDeploymentName"] ?? string.Empty,
            ProductAgentName = configuration["ProductAgentName"] ?? string.Empty,
            RegulatoryAgentName = configuration["RegulatoryAgentName"] ?? string.Empty,
            MaxPlanningIterations = configuration.GetValue("MaxPlanningIterations", 4)
        };

        Validate(settings);
        return settings;
    }

    private static void Validate(OrchestratorSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectEndpoint))
            throw new InvalidOperationException("FoundryProjectEndpoint is required.");
        if (string.IsNullOrWhiteSpace(settings.ModelDeploymentName))
            throw new InvalidOperationException("ModelDeploymentName is required.");
        if (string.IsNullOrWhiteSpace(settings.ProductAgentName))
            throw new InvalidOperationException("ProductAgentName is required.");
        if (string.IsNullOrWhiteSpace(settings.RegulatoryAgentName))
            throw new InvalidOperationException("RegulatoryAgentName is required.");
        if (settings.MaxPlanningIterations is < 1 or > 10)
            throw new InvalidOperationException("MaxPlanningIterations must be between 1 and 10.");
    }
}
