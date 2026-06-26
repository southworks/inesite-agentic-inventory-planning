using InventoryPlanning.Api.Host.Options;
using Microsoft.Extensions.Options;

namespace InventoryPlanning.Api.Host.Services;

public sealed class LoadedCaseDocument
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string DocumentPath { get; init; }

    public required string Reference { get; init; }

    public required BinaryData Content { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }
}

public sealed class CaseDocumentInfo
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string DocumentPath { get; init; }

    public required string Reference { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }
}

public sealed class LocalDocumentStorageService
{
    private static readonly HashSet<string> SupportedCaseIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "case-01",
        "case-02",
        "case-03",
        "case-04",
        "case-05"
    };

    private readonly DatasetOptions _options;
    private readonly ILogger<LocalDocumentStorageService> _logger;

    public LocalDocumentStorageService(
        IOptions<DatasetOptions> options,
        ILogger<LocalDocumentStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public static IReadOnlyCollection<string> GetSupportedCaseIds() => SupportedCaseIds;

    public string GetCaseDirectoryPath(string caseId)
    {
        EnsureSupportedCaseId(caseId);

        return Path.Combine(
            _options.RootPath,
            _options.CasesRelativePath,
            caseId.Trim(),
            _options.IngestSubfolder);
    }

    public string GetCaseDocumentPrefix(string caseId) =>
        NormalizeRelativePath(Path.Combine(
            _options.CasesRelativePath,
            caseId.Trim(),
            _options.IngestSubfolder));

    public Task<IReadOnlyList<CaseDocumentInfo>> ListCaseDocumentsAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSupportedCaseId(caseId);

        string caseDirectory = GetCaseDirectoryPath(caseId);
        if (!Directory.Exists(caseDirectory))
        {
            _logger.LogInformation(
                "Case ingest directory {CaseDirectory} does not exist for case {CaseId}.",
                caseDirectory,
                caseId);

            return Task.FromResult<IReadOnlyList<CaseDocumentInfo>>([]);
        }

        var documents = new List<CaseDocumentInfo>();

        foreach (string filePath in Directory.EnumerateFiles(caseDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo fileInfo = new(filePath);
            if (fileInfo.Length == 0)
            {
                continue;
            }

            string fileName = fileInfo.Name;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            documents.Add(CreateDocumentInfo(caseId, fileInfo));
        }

        _logger.LogInformation(
            "Listed {DocumentCount} document(s) for case {CaseId} from directory {CaseDirectory}.",
            documents.Count,
            caseId,
            caseDirectory);

        return Task.FromResult<IReadOnlyList<CaseDocumentInfo>>(documents);
    }

    public async Task<LoadedCaseDocument> GetCaseDocumentAsync(
        string caseId,
        string documentPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new InvalidOperationException("CaseId is required.");
        }

        if (string.IsNullOrWhiteSpace(documentPath))
        {
            throw new InvalidOperationException("DocumentPath is required.");
        }

        EnsureSupportedCaseId(caseId);

        string normalizedCaseId = caseId.Trim();
        string normalizedDocumentPath = NormalizeRelativePath(documentPath.Trim());
        string expectedPrefix = GetCaseDocumentPrefix(normalizedCaseId);

        if (!normalizedDocumentPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Document '{normalizedDocumentPath}' does not belong to case '{normalizedCaseId}'.");
        }

        string absolutePath = Path.Combine(
            _options.RootPath,
            normalizedDocumentPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolutePath))
        {
            throw new KeyNotFoundException(
                $"Document '{normalizedDocumentPath}' was not found for case '{normalizedCaseId}'.");
        }

        FileInfo fileInfo = new(absolutePath);
        if (fileInfo.Length == 0)
        {
            throw new KeyNotFoundException(
                $"Document '{normalizedDocumentPath}' is empty for case '{normalizedCaseId}'.");
        }

        byte[] content = await File.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);

        return new LoadedCaseDocument
        {
            FileName = fileInfo.Name,
            ContentType = ResolveContentType(fileInfo.Name),
            DocumentPath = normalizedDocumentPath,
            Reference = fileInfo.FullName,
            Content = BinaryData.FromBytes(content),
            LastModifiedUtc = fileInfo.LastWriteTimeUtc
        };
    }

    public async Task<IReadOnlyList<LoadedCaseDocument>> LoadCaseDocumentsAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CaseDocumentInfo> documentInfos =
            await ListCaseDocumentsAsync(caseId, cancellationToken).ConfigureAwait(false);

        var documents = new List<LoadedCaseDocument>(documentInfos.Count);

        foreach (CaseDocumentInfo documentInfo in documentInfos)
        {
            LoadedCaseDocument document = await GetCaseDocumentAsync(
                caseId,
                documentInfo.DocumentPath,
                cancellationToken).ConfigureAwait(false);

            documents.Add(document);
        }

        _logger.LogInformation(
            "Loaded {DocumentCount} document(s) for case {CaseId} from directory {CaseDirectory}.",
            documents.Count,
            caseId,
            GetCaseDirectoryPath(caseId));

        return documents;
    }

    private CaseDocumentInfo CreateDocumentInfo(string caseId, FileInfo fileInfo)
    {
        string documentPath = NormalizeRelativePath(
            Path.Combine(GetCaseDocumentPrefix(caseId), fileInfo.Name));

        return new CaseDocumentInfo
        {
            FileName = fileInfo.Name,
            ContentType = ResolveContentType(fileInfo.Name),
            DocumentPath = documentPath,
            Reference = fileInfo.FullName,
            LastModifiedUtc = fileInfo.LastWriteTimeUtc
        };
    }

    private static void EnsureSupportedCaseId(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new InvalidOperationException("CaseId is required.");
        }

        string normalizedCaseId = caseId.Trim();
        if (!SupportedCaseIds.Contains(normalizedCaseId))
        {
            throw new KeyNotFoundException(
                $"Case '{normalizedCaseId}' is not supported. Use one of: case-01, case-02, case-03, case-04, case-05.");
        }
    }

    private static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace('\\', '/');

    private static string ResolveContentType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
