using System.ComponentModel;
using Label.Agent.Orchestrator.Services;

namespace Label.Agent.Orchestrator.Tools;

public sealed class SpecialistAgentTools
{
    private readonly FoundryAgentGateway _agentGateway;
    private readonly AgentExecutionContext _executionContext;
    private readonly string _productAgentName;
    private readonly string _regulatoryAgentName;

    public SpecialistAgentTools(
        FoundryAgentGateway agentGateway,
        AgentExecutionContext executionContext,
        string productAgentName,
        string regulatoryAgentName)
    {
        _agentGateway = agentGateway;
        _executionContext = executionContext;
        _productAgentName = productAgentName;
        _regulatoryAgentName = regulatoryAgentName;
    }

    [Description(
        "Consults the Product Information Agent for product facts, " +
        "including SKU, ingredients, serving size, dosage, formulation, " +
        "directions, warnings, and product catalog information.")]
    public Task<string> AskProductInformationAgentAsync(
        [Description(
            "A complete, self-contained description of the product " +
            "information that must be retrieved.")]
        string request,
        CancellationToken cancellationToken = default)
    {
        AgentExecutionContext.RunContext context =
            _executionContext.Current;

        return _agentGateway.InvokeAgentAsync(
            _productAgentName,
            request,
            context.RunId,
            context.ConnectionId,
            cancellationToken);
    }

    [Description(
        "Consults the Regulatory Agent for dietary supplement labeling " +
        "requirements, claim restrictions, disclaimers, compliance " +
        "guidance, and regulatory supporting sources.")]
    public Task<string> AskRegulatoryAgentAsync(
        [Description(
            "A complete, self-contained regulatory question including " +
            "relevant product facts and proposed claims when available.")]
        string request,
        CancellationToken cancellationToken = default)
    {
        AgentExecutionContext.RunContext context =
            _executionContext.Current;

        return _agentGateway.InvokeAgentAsync(
            _regulatoryAgentName,
            request,
            context.RunId,
            context.ConnectionId,
            cancellationToken);
    }
}