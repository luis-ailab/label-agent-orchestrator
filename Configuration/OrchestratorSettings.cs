using Microsoft.Extensions.Configuration;

namespace Label.Agent.Orchestrator.Configuration;

public sealed class OrchestratorSettings
{
    public string ProjectEndpoint { get; set; } = string.Empty;

    public string ModelDeploymentName { get; set; } = string.Empty;

    public string ProductAgentName { get; set; } = string.Empty;

    public string RegulatoryAgentName { get; set; } = string.Empty;

    public static OrchestratorSettings Load()
    {
        IConfiguration config =
            new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

        return new OrchestratorSettings
        {
            ProjectEndpoint =
                config["FoundryProjectEndpoint"]!,

            ModelDeploymentName =
                config["ModelDeploymentName"]!,

            ProductAgentName =
                config["ProductAgentName"]!,

            RegulatoryAgentName =
                config["RegulatoryAgentName"]!
        };
    }
}