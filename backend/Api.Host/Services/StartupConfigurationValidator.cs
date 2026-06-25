using InventoryPlanning.Api.Host.Options;

namespace InventoryPlanning.Api.Host.Services;

public static class StartupConfigurationValidator
{
    public static void Validate(IConfiguration configuration)
    {
        var foundryOptions = new AzureFoundryOptions();
        configuration.GetSection(AzureFoundryOptions.SectionName).Bind(foundryOptions);

        string? foundryEndpoint = configuration["AZURE_FOUNDRY_PROJECT_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("AZURE_FOUNDRY_PROJECT_ENDPOINT")
            ?? foundryOptions.ProjectEndpoint;

        if (string.IsNullOrWhiteSpace(foundryEndpoint))
        {
            throw new InvalidOperationException(
                "Azure Foundry configuration is missing. Set AzureFoundry:ProjectEndpoint or AZURE_FOUNDRY_PROJECT_ENDPOINT.");
        }

        var storageOptions = new AzureBlobStorageOptions();
        configuration.GetSection(AzureBlobStorageOptions.SectionName).Bind(storageOptions);

        string? storageConnectionString = configuration["AZURE_STORAGE_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
            ?? storageOptions.ConnectionString;

        string? blobServiceUri = configuration["AZURE_STORAGE_BLOB_SERVICE_URI"]
            ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_BLOB_SERVICE_URI")
            ?? storageOptions.BlobServiceUri;

        if (string.IsNullOrWhiteSpace(storageConnectionString) && string.IsNullOrWhiteSpace(blobServiceUri))
        {
            throw new InvalidOperationException(
                "Azure Blob Storage is not configured. Set AzureStorage:ConnectionString, AzureStorage:BlobServiceUri, or the AZURE_STORAGE_CONNECTION_STRING / AZURE_STORAGE_BLOB_SERVICE_URI environment variables.");
        }

        if (string.IsNullOrWhiteSpace(storageOptions.ContainerName))
        {
            throw new InvalidOperationException("AzureStorage:ContainerName must be configured.");
        }
    }
}
