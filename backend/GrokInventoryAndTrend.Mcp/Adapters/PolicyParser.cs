using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrokInventoryAndTrend.Mcp.Models;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public sealed partial class PolicyParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex PolicyRefRegex = PolicyRefPattern();

    public IReadOnlyList<PolicyEntry> LoadFromJsonFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Policy JSON file was not found at '{filePath}'.");
        }

        var json = File.ReadAllText(filePath);
        return ParseJson(json);
    }

    public IReadOnlyList<PolicyEntry> ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize<PolicyJsonDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Policy JSON document could not be deserialized.");

        return document.Policies
            .Where(policy => !string.IsNullOrWhiteSpace(policy.PolicyRef))
            .Select(policy => policy.ToPolicyEntry())
            .ToArray();
    }

    public IReadOnlyList<PolicyEntry> Parse(string policyText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyText);

        var normalizedText = policyText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = normalizedText
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => block.StartsWith("Policy Ref:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = new List<PolicyEntry>(blocks.Length);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries);
            var policyRef = lines[0].Replace("Policy Ref:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            var rule = ExtractValue(lines, "Rule:");
            var threshold = ExtractValue(lines, "Threshold:");
            var action = ExtractValue(lines, "Action:");
            var exception = ExtractValue(lines, "Exception:");

            entries.Add(new PolicyEntry
            {
                PolicyRef = policyRef,
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

    [GeneratedRegex(@"Policy Ref:\s*([A-Z0-9\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyRefPattern();
}
