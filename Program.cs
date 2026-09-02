using Azure.AI.Projects;
using Azure.Identity;
using Label.Agent.Orchestrator.Configuration;
using Label.Agent.Orchestrator.Hubs;
using Label.Agent.Orchestrator.Services;
using Label.Agent.Orchestrator.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactDevelopment", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

OrchestratorSettings settings =
    OrchestratorSettings.Load();

var credential = new AzureCliCredential();

var projectClient = new AIProjectClient(
    new Uri(settings.ProjectEndpoint),
    credential);

builder.Services.AddSingleton(projectClient);
builder.Services.AddSingleton<RunEventPublisher>();
builder.Services.AddSingleton<AgentExecutionContext>();
builder.Services.AddSingleton<FoundryAgentGateway>();

builder.Services.AddSingleton(serviceProvider =>
{
    var gateway =
        serviceProvider.GetRequiredService<FoundryAgentGateway>();

    var executionContext =
        serviceProvider.GetRequiredService<AgentExecutionContext>();

    return new SpecialistAgentTools(
        gateway,
        executionContext,
        settings.ProductAgentName,
        settings.RegulatoryAgentName);
});

builder.Services.AddSingleton<AIAgent>(serviceProvider =>
{
    var specialistTools =
        serviceProvider.GetRequiredService<SpecialistAgentTools>();

    AIFunction productAgentTool =
        AIFunctionFactory.Create(
            specialistTools.AskProductInformationAgentAsync);

    AIFunction regulatoryAgentTool =
        AIFunctionFactory.Create(
            specialistTools.AskRegulatoryAgentAsync);

    const string orchestratorInstructions = """
    You are the Orchestrator Agent for the Life Extension
    Label Creation Platform.

    Analyze the user's request and invoke the appropriate
    specialist agents.

    Use the Product Information Agent for:
    - SKU and product identification
    - Ingredients and amounts
    - Serving size
    - Dosage and formulation
    - Existing product facts
    - Directions and warnings

    Use the Regulatory Agent for:
    - Dietary supplement labeling rules
    - Claim restrictions
    - Structure/function claims
    - Disease claim concerns
    - Required disclaimers
    - Regulatory requirements and sources

    Use both agents when regulatory guidance depends on
    product-specific facts.

    When both are required:
    1. Call the Product Information Agent first.
    2. Include relevant product facts in the request sent
       to the Regulatory Agent.
    3. Synthesize the returned information into one answer.

    Do not invent product information, regulatory requirements,
    citations, claims, ingredient amounts, or compliance conclusions.

    Clearly distinguish:
    - Verified product information
    - Regulatory guidance
    - Recommended conclusion
    - Missing information or required review

    Regulatory output is decision support and is not final
    legal or regulatory approval.
    """;

    return projectClient.AsAIAgent(
            model: settings.ModelDeploymentName,
            name: "LabelPlatformOrchestrator",
            instructions: orchestratorInstructions,
            tools:
            [
                productAgentTool,
                regulatoryAgentTool
            ]);
});
builder.Services.AddSingleton<ConversationSessionStore>();
builder.Services.AddSingleton<OrchestratorService>();

var app = builder.Build();

app.UseCors("ReactDevelopment");

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "Label.Agent.Orchestrator",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapHub<OrchestratorHub>("/hubs/orchestrator");

app.Run();