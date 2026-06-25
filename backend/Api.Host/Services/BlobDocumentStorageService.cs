using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using InventoryPlanning.Api.Host.Options;
using Microsoft.Extensions.Options;

namespace InventoryPlanning.Api.Host.Services;

public sealed class LoadedCaseDocument
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string BlobName { get; init; }

    public required string Reference { get; init; }

    public required BinaryData Content { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }
}

public sealed class CaseDocumentInfo
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string BlobName { get; init; }

    public required string Reference { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }
}

public sealed class BlobDocumentStorageService
{
    private const string CaseRootPrefix = "cases";

    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<BlobDocumentStorageService> _logger;

    public BlobDocumentStorageService(
        IOptions<AzureBlobStorageOptions> options,
        ILogger<BlobDocumentStorageService> logger)
    {
        _logger = logger;
        AzureBlobStorageOptions storageOptions = options.Value;

        BlobServiceClient blobServiceClient = CreateBlobServiceClient(storageOptions);
        _containerClient = blobServiceClient.GetBlobContainerClient(storageOptions.ContainerName);
    }

    public static string GetCasePrefix(string caseId) => $"{CaseRootPrefix}/{caseId.Trim()}/";

    public async Task<IReadOnlyList<CaseDocumentInfo>> ListCaseDocumentsAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        string prefix = GetCasePrefix(caseId);
        var documents = new List<CaseDocumentInfo>();

        await foreach (BlobItem blobItem in _containerClient
                           .GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (blobItem.Properties.ContentLength == 0)
            {
                continue;
            }

            string fileName = Path.GetFileName(blobItem.Name);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            BlobClient blobClient = _containerClient.GetBlobClient(blobItem.Name);

            documents.Add(new CaseDocumentInfo
            {
                FileName = fileName,
                ContentType = blobItem.Properties.ContentType ?? "application/octet-stream",
                BlobName = blobItem.Name,
                Reference = blobClient.Uri.ToString(),
                LastModifiedUtc = blobItem.Properties.LastModified ?? DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation(
            "Listed {DocumentCount} document(s) for case {CaseId} from prefix {CasePrefix}.",
            documents.Count,
            caseId,
            prefix);

        return documents;
    }

    public async Task<LoadedCaseDocument> GetCaseDocumentAsync(
        string caseId,
        string blobName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new InvalidOperationException("CaseId is required.");
        }

        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new InvalidOperationException("BlobName is required.");
        }

        string normalizedCaseId = caseId.Trim();
        string normalizedBlobName = blobName.Trim();
        string expectedPrefix = GetCasePrefix(normalizedCaseId);

        if (!normalizedBlobName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Blob '{normalizedBlobName}' does not belong to case '{normalizedCaseId}'.");
        }

        BlobClient blobClient = _containerClient.GetBlobClient(normalizedBlobName);
        Azure.Response<bool> exists = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);
        if (!exists.Value)
        {
            throw new KeyNotFoundException(
                $"Document '{normalizedBlobName}' was not found for case '{normalizedCaseId}'.");
        }

        BlobDownloadResult download = await blobClient
            .DownloadContentAsync(cancellationToken)
            .ConfigureAwait(false);

        return new LoadedCaseDocument
        {
            FileName = Path.GetFileName(normalizedBlobName),
            ContentType = download.Details.ContentType ?? "application/octet-stream",
            BlobName = normalizedBlobName,
            Reference = blobClient.Uri.ToString(),
            Content = download.Content,
            LastModifiedUtc = download.Details.LastModified
        };
    }

    public async Task<IReadOnlyList<LoadedCaseDocument>> LoadCaseDocumentsAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        string prefix = GetCasePrefix(caseId);
        var documents = new List<LoadedCaseDocument>();

        await foreach (BlobItem blobItem in _containerClient
                           .GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (blobItem.Properties.ContentLength == 0)
            {
                continue;
            }

            BlobClient blobClient = _containerClient.GetBlobClient(blobItem.Name);
            BlobDownloadResult download = await blobClient
                .DownloadContentAsync(cancellationToken)
                .ConfigureAwait(false);

            string fileName = Path.GetFileName(blobItem.Name);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            documents.Add(new LoadedCaseDocument
            {
                FileName = fileName,
                ContentType = download.Details.ContentType ?? "application/octet-stream",
                BlobName = blobItem.Name,
                Reference = blobClient.Uri.ToString(),
                Content = download.Content,
                LastModifiedUtc = blobItem.Properties.LastModified ?? DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation(
            "Loaded {DocumentCount} document(s) for case {CaseId} from prefix {CasePrefix}.",
            documents.Count,
            caseId,
            prefix);

        return documents;
    }

    private static BlobServiceClient CreateBlobServiceClient(AzureBlobStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(options.BlobServiceUri))
        {
            return new BlobServiceClient(new Uri(options.BlobServiceUri), new Azure.Identity.DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "Azure Blob Storage is not configured. Set AzureStorage:ConnectionString or AzureStorage:BlobServiceUri.");
    }
}
