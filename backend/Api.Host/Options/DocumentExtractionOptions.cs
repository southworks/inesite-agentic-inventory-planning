namespace InventoryPlanning.Api.Host.Options;

public sealed class DocumentExtractionOptions
{
    public const string SectionName = "DocumentExtraction";

    /// <summary>
    /// When true, only plain text files are extracted. PDF and image extraction is skipped.
    /// Set to false and configure Endpoint to enable Azure Document Intelligence extraction.
    /// </summary>
    public bool TextOnlyMode { get; set; } = true;

    /// <summary>
    /// Azure Document Intelligence endpoint, for example https://{resource}.cognitiveservices.azure.com/.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
}
