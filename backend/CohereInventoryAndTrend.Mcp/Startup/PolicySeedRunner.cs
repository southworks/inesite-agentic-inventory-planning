using CohereInventoryAndTrend.Mcp.Adapters;

namespace CohereInventoryAndTrend.Mcp.Startup;

public sealed class PolicySeedRunner
{
    private readonly SearchIndexInitializer _searchIndexInitializer;
    private readonly PolicyIndexSeeder _policyIndexSeeder;
    private readonly ILogger<PolicySeedRunner> _logger;

    public PolicySeedRunner(
        SearchIndexInitializer searchIndexInitializer,
        PolicyIndexSeeder policyIndexSeeder,
        ILogger<PolicySeedRunner> logger)
    {
        _searchIndexInitializer = searchIndexInitializer;
        _policyIndexSeeder = policyIndexSeeder;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Policy seed mode: ensuring Azure AI Search indexes exist.");
            await _searchIndexInitializer.EnsureIndexesAsync(cancellationToken);

            _logger.LogInformation("Policy seed mode: seeding policy index if needed.");
            await _policyIndexSeeder.SeedIfNeededAsync(cancellationToken);

            _logger.LogInformation("Policy seed mode completed successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Policy seed mode failed.");
            return 1;
        }
    }
}
