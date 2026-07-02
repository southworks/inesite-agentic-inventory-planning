using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using GrokInventoryAndTrend.Api.Options;
using Microsoft.Extensions.Options;

namespace GrokInventoryAndTrend.Api.Services;

public sealed class DocumentTextExtractionService
{
    private readonly DocumentExtractionOptions _options;
    private readonly ILogger<DocumentTextExtractionService> _logger;
    private readonly DocumentIntelligenceClient? _documentIntelligenceClient;

    public DocumentTextExtractionService(
        IOptions<DocumentExtractionOptions> options,
        ILogger<DocumentTextExtractionService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!_options.TextOnlyMode && !string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            _documentIntelligenceClient = new DocumentIntelligenceClient(
                new Uri(_options.Endpoint),
                new DefaultAzureCredential());
        }
    }

    public async Task<IReadOnlyList<NormalizedCaseDocument>> ExtractAsync(
        IReadOnlyList<LoadedCaseDocument> documents,
        CancellationToken cancellationToken)
    {
        var results = new List<NormalizedCaseDocument>(documents.Count);

        foreach (LoadedCaseDocument document in documents)
        {
            results.Add(await ExtractSingleAsync(document, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<NormalizedCaseDocument> ExtractSingleAsync(
        LoadedCaseDocument document,
        CancellationToken cancellationToken)
    {
        if (document.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizedCaseDocument.FromLoaded(
                document,
                document.Content.ToString(),
                DocumentExtractionModes.PlainText,
                extractionSucceeded: true);
        }

        if (_options.TextOnlyMode || _documentIntelligenceClient is null)
        {
            return NormalizedCaseDocument.FromLoaded(
                document,
                string.Empty,
                DocumentExtractionModes.Unsupported,
                extractionSucceeded: false,
                extractionMessage:
                    "Advanced extraction is disabled. Enable DocumentExtraction:TextOnlyMode=false and configure DocumentExtraction:Endpoint for PDF and image OCR.");
        }

        try
        {
            string extractionMode = IsPdf(document.ContentType)
                ? DocumentExtractionModes.PdfText
                : IsImage(document.ContentType)
                    ? DocumentExtractionModes.Ocr
                    : DocumentExtractionModes.Unsupported;

            if (extractionMode == DocumentExtractionModes.Unsupported)
            {
                return NormalizedCaseDocument.FromLoaded(
                    document,
                    string.Empty,
                    DocumentExtractionModes.Unsupported,
                    extractionSucceeded: false,
                    extractionMessage: $"Content type '{document.ContentType}' is not supported for Azure extraction.");
            }

            AnalyzeResult analyzeResult = (await _documentIntelligenceClient
                .AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    "prebuilt-read",
                    document.Content,
                    cancellationToken)
                .ConfigureAwait(false)).Value;

            string extractedText = analyzeResult.Content?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return NormalizedCaseDocument.FromLoaded(
                    document,
                    string.Empty,
                    DocumentExtractionModes.Failed,
                    extractionSucceeded: false,
                    extractionMessage: "Azure Document Intelligence returned no text for this document.");
            }

            _logger.LogInformation(
                "Extracted {CharacterCount} character(s) from {FileName} using {ExtractionMode}.",
                extractedText.Length,
                document.FileName,
                extractionMode);

            return NormalizedCaseDocument.FromLoaded(
                document,
                extractedText,
                extractionMode,
                extractionSucceeded: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract text from {FileName} with content type {ContentType}.",
                document.FileName,
                document.ContentType);

            return NormalizedCaseDocument.FromLoaded(
                document,
                string.Empty,
                DocumentExtractionModes.Failed,
                extractionSucceeded: false,
                extractionMessage: ex.Message);
        }
    }

    private static bool IsPdf(string contentType) =>
        string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsImage(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
