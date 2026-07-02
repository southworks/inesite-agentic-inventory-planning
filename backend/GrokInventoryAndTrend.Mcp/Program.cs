using System.Collections.Concurrent;
using GrokInventoryAndTrend.Mcp;
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
    bootstrapBuilder.Services.AddApplicationInsightsTelemetry();
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

builder.Services.AddApplicationInsightsTelemetry();

builder.Configuration.AddJsonFile("appsettings.Deployment.local.json", optional: true, reloadOnChange: true);

builder.Services.AddInventoryPlanningMcpServices(builder.Configuration);

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
