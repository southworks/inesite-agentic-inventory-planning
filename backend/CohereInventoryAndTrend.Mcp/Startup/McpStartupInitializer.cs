using CohereInventoryAndTrend.Mcp.Adapters;
using CohereInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace CohereInventoryAndTrend.Mcp.Startup;

public sealed class McpStartupInitializer : IHostedService
{
    private readonly SearchIndexInitializer _searchIndexInitializer;
    private readonly PolicyIndexSeeder _policyIndexSeeder;
    private readonly McpStartupOptions _startupOptions;
    private readonly ILogger<McpStartupInitializer> _logger;

    public McpStartupInitializer(
        SearchIndexInitializer searchIndexInitializer,
        PolicyIndexSeeder policyIndexSeeder,
        IOptions<McpStartupOptions> startupOptions,
        ILogger<McpStartupInitializer> logger)
    {
        _searchIndexInitializer = searchIndexInitializer;
        _policyIndexSeeder = policyIndexSeeder;
        _startupOptions = startupOptions.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_startupOptions.EnsureSearchIndexesOnStartup)
        {
            _logger.LogInformation("Ensuring Azure AI Search indexes exist.");
            await _searchIndexInitializer.EnsureIndexesAsync(cancellationToken);
        }

        if (_startupOptions.SeedPoliciesOnStartup)
        {
            _logger.LogInformation("Seeding policy index if needed.");
            await _policyIndexSeeder.SeedIfNeededAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Policy seeding at startup is disabled. Use deploy-time seed mode or enable McpStartup:SeedPoliciesOnStartup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
