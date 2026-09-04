using Azure.AI.Projects;
using Azure.Identity;
using Label.Agent.Orchestrator.Configuration;
using Label.Agent.Orchestrator.Hubs;
using Label.Agent.Orchestrator.Planning;
using Label.Agent.Orchestrator.Services;
using Label.Agent.Orchestrator.Workflows;
using Microsoft.Agents.AI;
using Label.Agent.Orchestrator.TemplateIntelligence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactDevelopment", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

OrchestratorSettings settings = OrchestratorSettings.Load(builder.Configuration);
var credential = new AzureCliCredential();
var projectClient = new AIProjectClient(new Uri(settings.ProjectEndpoint), credential);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(projectClient);
builder.Services.AddSingleton<ConversationSessionStore>();
builder.Services.AddSingleton<RunEventPublisher>();
builder.Services.AddSingleton<FoundryAgentGateway>();
builder.Services.AddSingleton<PlannerResponseParser>();

builder.Services.AddSingleton<AIAgent>(_ =>
    projectClient.AsAIAgent(
        model: settings.ModelDeploymentName,
        name: "LabelPlatformPlanner",
        instructions: PlannerPrompts.Instructions));

builder.Services.AddSingleton<PlannerAgentService>();
builder.Services.AddSingleton<LabelWorkflowRunner>();
builder.Services.AddSingleton<OrchestratorService>();
builder.Services.AddHttpClient<TemplateIntelligenceClient>(client =>
{
    var baseUrl =
        builder.Configuration["Services:TemplateIntelligence:BaseUrl"]
        ?? throw new InvalidOperationException(
            "Template Intelligence BaseUrl is missing.");

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});
var app = builder.Build();
app.UseCors("ReactDevelopment");

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "Label.Agent.Orchestrator",
    mode = "PlannerWorkflow",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapHub<OrchestratorHub>("/hubs/orchestrator");
app.Run();
