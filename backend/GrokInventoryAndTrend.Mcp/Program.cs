using System.Collections.Concurrent;
using System.Security.Claims;
using AgentGovernance.Extensions.ModelContextProtocol;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using GrokInventoryAndTrend.Mcp;
using GrokInventoryAndTrend.Mcp.Governance;
using GrokInventoryAndTrend.Mcp.Options;
using GrokInventoryAndTrend.Mcp.Startup;
using ModelContextProtocol.Server;

if (args.Contains("--bootstrap-foundry-iq", StringComparer.OrdinalIgnoreCase))
{
    var bootstrapBuilder = WebApplication.CreateBuilder(args);
    bootstrapBuilder.Logging.ClearProviders();
    bootstrapBuilder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });
    bootstrapBuilder.Logging.SetMinimumLevel(LogLevel.Information);
    ConfigureAzureMonitorTelemetry(bootstrapBuilder);
    bootstrapBuilder.Configuration.AddJsonFile("appsettings.Deployment.local.json", optional: true, reloadOnChange: true);
    bootstrapBuilder.Configuration.AddJsonFile("appsettings.Bootstrap.local.json", optional: true, reloadOnChange: true);
    bootstrapBuilder.Configuration.AddEnvironmentVariables();
    bootstrapBuilder.Services.AddInventoryPlanningMcpServices(bootstrapBuilder.Configuration);
    bootstrapBuilder.Services.Configure<FoundryIqBootstrapOptions>(
        bootstrapBuilder.Configuration.GetSection(FoundryIqBootstrapOptions.SectionName));
    bootstrapBuilder.Services.AddSingleton<FoundryIqBootstrapRunner>();

    var bootstrapApp = bootstrapBuilder.Build();
    var exitCode = await bootstrapApp.Services
        .GetRequiredService<FoundryIqBootstrapRunner>()
        .RunAsync(CancellationToken.None);

    await bootstrapApp.DisposeAsync();
    Environment.ExitCode = exitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);

ConfigureAzureMonitorTelemetry(builder);

builder.Configuration.AddJsonFile("appsettings.Deployment.local.json", optional: true, reloadOnChange: true);

builder.Services.AddInventoryPlanningMcpServices(builder.Configuration);

var toolDictionary = new ConcurrentDictionary<string, McpServerTool[]>(StringComparer.OrdinalIgnoreCase);
var governancePolicyPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "governance",
    "policies",
    "mcp.yaml");

builder.Services.AddMcpServer()
    .WithGovernance(options =>
    {
        options.PolicyPaths.Add(governancePolicyPath);
        options.ServerName = "grok-inventory-mcp";
        options.DefaultAgentId = "did:mcp:inventory-planning";
        options.RequireAuthenticatedAgentId = false;
        options.AgentIdResolver = static principal =>
            principal.FindFirst(McpEndpointIdentity.AgentIdClaimType)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        options.EnableAudit = true;
        options.EnableMetrics = true;
    })
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
        options.ConfigureSessionOptions = (httpContext, mcpOptions, _) =>
        {
            var path = httpContext.Request.Path.Value ?? string.Empty;
            var serverKey = McpEndpointIdentity.ResolveServerKey(path);

            if (!toolDictionary.TryGetValue(serverKey, out var tools))
            {
                return Task.CompletedTask;
            }

            mcpOptions.ToolCollection = [];
            foreach (var tool in tools)
            {
                mcpOptions.ToolCollection.Add(tool);
            }

            return Task.CompletedTask;
        };
    });

var app = builder.Build();

ServiceCollectionExtensions.PopulateToolDictionary(app.Services, toolDictionary);

app.UseMiddleware<McpAgentIdentityMiddleware>();

app.MapMcp("/signal-ingestion/mcp");
app.MapMcp("/feature-and-causality/mcp");
app.MapMcp("/forecasting/mcp");
app.MapMcp("/replenishment-and-allocation/mcp");
app.MapMcp("/planner-copilot/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static void ConfigureAzureMonitorTelemetry(WebApplicationBuilder builder)
{
    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
    {
        builder.Services.AddOpenTelemetry().UseAzureMonitor();
    }
}
