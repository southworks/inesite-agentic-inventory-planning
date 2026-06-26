using CohereInventoryAndTrend.Mcp.Options;

namespace CohereInventoryAndTrend.Mcp.Adapters;

internal static class FoundryEndpointBuilder
{
    private const string EmbeddingsApiVersion = "2024-05-01-preview";
    private const string OpenAiDeploymentsPath = "/openai/deployments/";
    private const string CohereRerankPath = "/providers/cohere/v2/rerank";

    public static string BuildEmbeddingsUrl(AzureFoundryModelsOptions options)
    {
        var embedBase = NormalizeEmbedBase(options.EmbedEndpoint, options.EmbedDeploymentName);
        return $"{embedBase}/embeddings?api-version={EmbeddingsApiVersion}";
    }

    public static string BuildRerankUrl(AzureFoundryModelsOptions options)
    {
        var endpoint = options.RerankEndpoint.TrimEnd('/');

        if (endpoint.Contains(CohereRerankPath, StringComparison.OrdinalIgnoreCase)
            || endpoint.EndsWith("/rerank", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var accountBase = StripOpenAiDeploymentPath(endpoint);
        return $"{accountBase}{CohereRerankPath}";
    }

    private static string NormalizeEmbedBase(string endpoint, string deploymentName)
    {
        var normalized = endpoint.TrimEnd('/');

        foreach (var suffix in new[] { "/v1/embed", "/v1/embeddings", "/embeddings" })
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        if (normalized.Contains(OpenAiDeploymentsPath, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            throw new InvalidOperationException(
                "AzureFoundryModels:EmbedDeploymentName is required when EmbedEndpoint is an account base URL.");
        }

        return $"{normalized}{OpenAiDeploymentsPath}{deploymentName}";
    }

    private static string StripOpenAiDeploymentPath(string url)
    {
        var index = url.IndexOf(OpenAiDeploymentsPath, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? url[..index] : url;
    }
}
