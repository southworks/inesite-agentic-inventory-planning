using GrokInventoryAndTrend.Mcp.Adapters;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Startup;

public sealed class McpStartupInitializer : IHostedService
{
    private readonly SearchIndexInitializer _searchIndexInitializer;
    private readonly McpStartupOptions _startupOptions;
    private readonly ILogger<McpStartupInitializer> _logger;

    public McpStartupInitializer(
        SearchIndexInitializer searchIndexInitializer,
        IOptions<McpStartupOptions> startupOptions,
        ILogger<McpStartupInitializer> logger)
    {
        _searchIndexInitializer = searchIndexInitializer;
        _startupOptions = startupOptions.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_startupOptions.EnsureSearchIndexesOnStartup)
        {
            _logger.LogInformation("Ensuring Azure AI Search evidence index exists.");
            await _searchIndexInitializer.EnsureIndexesAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
