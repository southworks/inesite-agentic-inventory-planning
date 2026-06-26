using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CohereInventoryAndTrend.Mcp.Models;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public sealed partial class KnowledgeEntryParser
{
    private static readonly Regex KnowledgeRefRegex = KnowledgeRefPattern();

    public IReadOnlyList<KnowledgeEntry> Parse(string knowledgeText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(knowledgeText);

        var normalizedText = knowledgeText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = normalizedText
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => block.StartsWith("Knowledge Ref:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = new List<KnowledgeEntry>(blocks.Length);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var knowledgeRef = lines[0].Replace("Knowledge Ref:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            var rule = ExtractValue(lines, "Rule:");
            var threshold = ExtractValue(lines, "Threshold:");
            var action = ExtractValue(lines, "Action:");
            var exception = ExtractValue(lines, "Exception:");

            entries.Add(new KnowledgeEntry
            {
                KnowledgeRef = knowledgeRef,
                Rule = rule,
                Threshold = threshold,
                Action = action,
                Exception = exception,
                FullText = block.Trim()
            });
        }

        return entries;
    }

    public static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string ExtractValue(IEnumerable<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line is null ? string.Empty : line[prefix.Length..].Trim();
    }

    [GeneratedRegex(@"Knowledge Ref:\s*([A-Z0-9\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex KnowledgeRefPattern();
}
