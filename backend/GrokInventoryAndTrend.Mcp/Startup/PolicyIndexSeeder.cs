using GrokInventoryAndTrend.Mcp.Adapters;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Mcp.Startup;

public sealed class PolicyIndexSeeder
{
    private readonly PolicyParser _policyParser;
    private readonly PolicyIndexAdapter _policyIndexAdapter;
    private readonly DatasetOptions _datasetOptions;
    private readonly ILogger<PolicyIndexSeeder> _logger;

    public PolicyIndexSeeder(
        PolicyParser policyParser,
        PolicyIndexAdapter policyIndexAdapter,
        IOptions<DatasetOptions> datasetOptions,
        IHostEnvironment environment,
        ILogger<PolicyIndexSeeder> logger)
    {
        _policyParser = policyParser;
        _policyIndexAdapter = policyIndexAdapter;
        _datasetOptions = datasetOptions.Value;
        _datasetOptions.PolicyFilePath = ResolveContentPath(
            environment.ContentRootPath,
            _datasetOptions.PolicyFilePath);
        _logger = logger;
    }

    public async Task SeedIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_datasetOptions.PolicyFilePath))
        {
            throw new FileNotFoundException(
                $"Policy file was not found at '{_datasetOptions.PolicyFilePath}'.");
        }

        var policyText = await File.ReadAllTextAsync(_datasetOptions.PolicyFilePath, cancellationToken);
        var contentHash = PolicyParser.ComputeContentHash(policyText);
        var storedHash = await _policyIndexAdapter.GetStoredContentHashAsync(cancellationToken);

        if (string.Equals(storedHash, contentHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Policy index is up to date. Skipping reindex.");
            return;
        }

        var policies = _policyParser.Parse(policyText);
        await _policyIndexAdapter.SeedPoliciesAsync(policies, contentHash, cancellationToken);
        _logger.LogInformation("Seeded {PolicyCount} policies into the policy index.", policies.Count);
    }

    private static string ResolveContentPath(string contentRootPath, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(contentRootPath, path));
    }
}
