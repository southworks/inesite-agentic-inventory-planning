using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Files.DataLake;
using GrokInventoryAndTrend.Mcp.Options;
using Microsoft.Extensions.Logging;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed class FabricLakehouseClient : IFabricLakehouseClient, IDisposable
{
    private const string FabricOneLakeEndpoint = "https://onelake.dfs.fabric.microsoft.com";
    private const string FabricBlobEndpoint = "https://onelake.blob.fabric.microsoft.com";
    private const string LakehouseSuffix = ".lakehouse";

    private readonly DataLakeFileSystemClient _fileSystemClient;
    private readonly string _lakehouseName;
    private readonly string _workspaceName;
    private readonly DefaultAzureCredential _credential;
    private readonly int _timeoutSeconds;
    private readonly ILogger<FabricLakehouseClient> _logger;

    private FabricLakehouseClient(
        DataLakeFileSystemClient fileSystemClient,
        string lakehouseName,
        string workspaceName,
        DefaultAzureCredential credential,
        int timeoutSeconds,
        ILogger<FabricLakehouseClient> logger)
    {
        _fileSystemClient = fileSystemClient;
        _lakehouseName = lakehouseName;
        _workspaceName = workspaceName;
        _credential = credential;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
    }

    public static FabricLakehouseClient Create(DataSourceOptions options, ILogger<FabricLakehouseClient> logger)
    {
        var workspaceName = options.FabricLakehouse?.WorkspaceName;
        var lakehouseName = options.FabricLakehouse?.LakehouseName;
        if (string.IsNullOrWhiteSpace(workspaceName) || string.IsNullOrWhiteSpace(lakehouseName))
        {
            throw new InvalidOperationException(
                "FabricLakehouse:WorkspaceName and FabricLakehouse:LakehouseName are required when DataSource:Mode is Fabric.");
        }

        var credential = new DefaultAzureCredential();
        var serviceUri = new Uri($"{FabricOneLakeEndpoint}/{workspaceName}");
        var serviceClient = new DataLakeServiceClient(serviceUri, credential);
        var fileSystemClient = serviceClient.GetFileSystemClient(lakehouseName + LakehouseSuffix);
        var timeoutSeconds = options.FabricLakehouse?.TimeoutSeconds ?? 30;

        logger.LogInformation(
            "FabricLakehouseClient initialized against workspace {WorkspaceName} lakehouse {LakehouseName}{Suffix} timeout {TimeoutSeconds}s.",
            workspaceName,
            lakehouseName,
            LakehouseSuffix,
            timeoutSeconds);

        return new FabricLakehouseClient(fileSystemClient, lakehouseName, workspaceName, credential, timeoutSeconds, logger);
    }

    public async Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var (directory, fileName) = SplitPath(relativePath);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var directoryClient = _fileSystemClient.GetDirectoryClient(directory);
        var fileClient = directoryClient.GetFileClient(fileName);
        try
        {
            var response = await fileClient.ExistsAsync(linkedCts.Token).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<string> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var blobUri = new Uri($"{FabricBlobEndpoint}/{_workspaceName}/{_lakehouseName}{LakehouseSuffix}/{relativePath.TrimStart('/')}");
        var blobClient = new BlobClient(blobUri, _credential);
        try
        {
            var response = await blobClient.DownloadContentAsync(linkedCts.Token).ConfigureAwait(false);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException(
                $"Fabric file '{relativePath}' was not found in lakehouse '{_lakehouseName}{LakehouseSuffix}'.",
                ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string relativeDirectory, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var enumerable = _fileSystemClient.GetPathsAsync(relativeDirectory, recursive: true, userPrincipalName: false, linkedCts.Token);
        await foreach (var pathItem in enumerable.WithCancellation(linkedCts.Token).ConfigureAwait(false))
        {
            if (pathItem.IsDirectory == true)
            {
                continue;
            }

            results.Add(pathItem.Name);
        }

        return results;
    }

    private static (string Directory, string FileName) SplitPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path is required.", nameof(relativePath));
        }

        var trimmed = relativePath.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash < 0)
        {
            throw new ArgumentException(
                $"Path '{relativePath}' must include a directory and a file name separated by '/'.",
                nameof(relativePath));
        }

        return (trimmed[..lastSlash], trimmed[(lastSlash + 1)..]);
    }

    public void Dispose()
    {
    }
}
