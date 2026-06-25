namespace InventoryPlanning.Api.Host.Services;

public sealed class NormalizedCaseDocument
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string BlobName { get; init; }

    public required string Reference { get; init; }

    public required DateTimeOffset LastModifiedUtc { get; init; }

    public required string ExtractedText { get; init; }

    public required string ExtractionMode { get; init; }

    public bool ExtractionSucceeded { get; init; }

    public string? ExtractionMessage { get; init; }

    public static NormalizedCaseDocument FromLoaded(
        LoadedCaseDocument document,
        string extractedText,
        string extractionMode,
        bool extractionSucceeded,
        string? extractionMessage = null) =>
        new()
        {
            FileName = document.FileName,
            ContentType = document.ContentType,
            BlobName = document.BlobName,
            Reference = document.Reference,
            LastModifiedUtc = document.LastModifiedUtc,
            ExtractedText = extractedText,
            ExtractionMode = extractionMode,
            ExtractionSucceeded = extractionSucceeded,
            ExtractionMessage = extractionMessage
        };
}

public static class DocumentExtractionModes
{
    public const string PlainText = "plain_text";

    public const string PdfText = "pdf_text";

    public const string Ocr = "ocr";

    public const string Unsupported = "unsupported";

    public const string Failed = "failed";
}
