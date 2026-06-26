using System.Collections.Concurrent;
using CohereInventoryAndTrend.Mcp;
using CohereInventoryAndTrend.Mcp.Startup;
using ModelContextProtocol.Server;

if (args.Contains("--seed-policies", StringComparer.OrdinalIgnoreCase))
{
    var seedBuilder = WebApplication.CreateBuilder(args);
    seedBuilder.Configuration.AddJsonFile("appsettings.Deployment.local.json", optional: true, reloadOnChange: true);
    seedBuilder.Configuration.AddJsonFile("appsettings.Seed.local.json", optional: true, reloadOnChange: true);
    seedBuilder.Services.AddInventoryPlanningMcpServices(seedBuilder.Configuration);

    var seedApp = seedBuilder.Build();
    var exitCode = await seedApp.Services
        .GetRequiredService<PolicySeedRunner>()
        .RunAsync(CancellationToken.None);

    await seedApp.DisposeAsync();
    Environment.ExitCode = exitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Deployment.local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("appsettings.Seed.local.json", optional: true, reloadOnChange: true);

builder.Services.AddInventoryPlanningMcpServices(builder.Configuration);
builder.Services.AddHostedService<McpStartupInitializer>();

var toolDictionary = new ConcurrentDictionary<string, McpServerTool[]>(StringComparer.OrdinalIgnoreCase);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
        options.ConfigureSessionOptions = (httpContext, mcpOptions, _) =>
        {
            var path = httpContext.Request.Path.Value ?? string.Empty;
            var serverKey = ResolveServerKey(path);

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

app.MapMcp("/signal-ingestion/mcp");
app.MapMcp("/feature-and-causality/mcp");
app.MapMcp("/forecasting/mcp");
app.MapMcp("/replenishment-and-allocation/mcp");
app.MapMcp("/planner-copilot/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static string ResolveServerKey(string path)
{
    if (path.Contains("/signal-ingestion/", StringComparison.OrdinalIgnoreCase))
    {
        return "signal-ingestion";
    }

    if (path.Contains("/feature-and-causality/", StringComparison.OrdinalIgnoreCase))
    {
        return "feature-and-causality";
    }

    if (path.Contains("/forecasting/", StringComparison.OrdinalIgnoreCase))
    {
        return "forecasting";
    }

    if (path.Contains("/replenishment-and-allocation/", StringComparison.OrdinalIgnoreCase))
    {
        return "replenishment-and-allocation";
    }

    if (path.Contains("/planner-copilot/", StringComparison.OrdinalIgnoreCase))
    {
        return "planner-copilot";
    }

    return "signal-ingestion";
}
