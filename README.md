Before running the Label Creation Platform, the following Azure resources must be created and configured:

Required Azure Resources
Azure AI Foundry Project
Azure OpenAI deployment
Azure AI Search (vector database which indexes the regulations stored in SharePoint)
SharePoint (to host the regulations)

Azure Container Apps for:
Label.Mcp.Product
Label.Mcp.Regulatory


Foundry Agent Setup

The orchestration layer depends on two AI Foundry Agents that must be created within your Azure AI Foundry project.

Agent 1: Product-Information-Agent
Agent Name
Product-Information-Agent

Instructions
You are the Product Information Agent for VitaNova Labs.

Your responsibility is to retrieve product information from the Product MCP server.

Whenever product data is required, use the available MCP tools rather than making assumptions.

Always return factual information exactly as provided by the MCP tools.

Tools

Attach the Product MCP tool endpoint provided by:

Label.Mcp.Product


This service must be deployed to Azure as a Container App before configuring the agent.

Agent 2: Regulatory-Agent
Agent Name
Regulatory-Agent

Instructions
You are the Regulatory Agent for the Label Creation Platform.

Your purpose is to support label generation and label validation.

Always use the Regulatory MCP tools before answering.

Your responsibility is to identify regulatory requirements that impact a label.

Focus on:

• Required label statements
• Required warnings
• Mandatory disclosures
• Ingredient restrictions
• Market restrictions
• Allowed claims
• Prohibited claims
• Compliance requirements
• Severity levels
• Regulatory citations

Do not generate long narrative reports.

Do not repeat information.

When a specific regulation is requested, summarize only that regulation unless additional regulations are explicitly referenced as dependencies.

When regulations are found, present the information using the following structure:

REGULATION
SUMMARY
REQUIREMENTS
WARNINGS
RESTRICTIONS
ALLOWED CLAIMS
PROHIBITED CLAIMS
RELATED REGULATIONS
SOURCE DOCUMENTS

Return concise, implementation-focused guidance.

Use regulation identifiers (for example VNL-R001) as citations instead of platform-generated citation strings.

If information cannot be found, explicitly state that no applicable regulation was located.

Tools

Attach the Regulatory MCP tool endpoint provided by:

Label.Mcp.Regulatory


This service must be deployed to Azure as a Container App before configuring the agent.

Deploy MCP Services

Before creating the Foundry Agents, deploy the following services:

Label.Mcp.Product
Label.Mcp.Regulatory


Both services should be deployed as independent Azure Container Apps.

After deployment, configure the corresponding MCP tool connections in Azure AI Foundry and associate them with the appropriate agents.

Configure appsettings.json

Create an appsettings.json file from the provided template:

cp appsettings.example.json appsettings.json


Populate the values with your Azure resources.


Architecture Dependencies

The orchestrator requires the following components to be available:

Label.Agent.Orchestrator
│
├── Product-Information-Agent (Azure AI Foundry)
│   └── ProductMCP
│       └── Label.Mcp.Product (Container App)
│
└── Regulatory-Agent (Azure AI Foundry)
    └── RegulatoryMCP
        └── Label.Mcp.Regulatory (Container App)


The orchestrator invokes the Foundry Agents, which in turn retrieve authoritative information from their respective MCP services. This ensures that product and regulatory responses are grounded in enterprise data and regulatory knowledge rather than model-generated assumptions.


Local Development and Testing

For local development, the orchestrator depends on several supporting services that must be running before end-to-end workflows can be executed.

Required Local Services

Start the following projects locally:

Label.Service.TemplateIntelligence
Label.Service.Evaluation
Label.Service.BeamSearch
Label.Agent.Generation


These components provide the core functionality used by the orchestration workflow:

Label.Service.TemplateIntelligence

Responsible for:

Label template discovery
Template recommendation
Template metadata retrieval
Template matching against product requirements
Label.Agent.Generation

Responsible for:

Creating candidate label content
Combining product information and regulatory guidance
Producing multiple label generation alternatives
Label.Service.Evaluation

Responsible for:

Evaluating generated label candidates
Scoring label quality
Identifying missing regulatory requirements
Ranking candidate responses
Label.Service.BeamSearch

Responsible for:

Managing Tree of Thoughts exploration
Expanding candidate branches
Maintaining beam search state
Selecting the highest-scoring label paths
Local Development Architecture

The following components must be available during local testing:

Label.Web
    │
    ▼
Label.Agent.Orchestrator
    │
    ├── Product-Information-Agent (Foundry)
    │       │
    │       ▼
    │   ProductMCP
    │       │
    │       ▼
    │   Label.Mcp.Product (running on a Container App for Foundry to access)
    │
    ├── Regulatory-Agent (Foundry)
    │       │
    │       ▼
    │   RegulatoryMCP
    │       │
    │       ▼
    │   Label.Mcp.Regulatory (running on a Container App for Foundry to access)
    │
    ├── Label.Service.TemplateIntelligence
    │
    ├── Label.Agent.Generation
    │
    ├── Label.Service.Evaluation
    │
    └── Label.Service.BeamSearch


For a complete workflow execution, all of the above services should be running simultaneously.

Future Azure Deployment

While these services are executed locally during development and testing, the target production architecture is for each component to be independently deployed as an Azure Container App.

Planned Container Apps
Label.Agent.Orchestrator
Label.Mcp.Product
Label.Mcp.Regulatory
Label.Service.TemplateIntelligence
Label.Agent.Generation
Label.Service.Evaluation
Label.Service.BeamSearch
Label.Web


Deploying each component independently provides:

Independent scaling
Independent deployment cycles
Service isolation
Improved resiliency
Clear separation of responsibilities
Support for future agent and workflow expansion

This architecture aligns with the platform's long-term goal of running a fully distributed, containerized, multi-agent label creation system within Azure Container Apps.