using System.Text.RegularExpressions;

namespace GrokInventoryAndTrend.Mcp.Adapters;

public static partial class CaseReadmeReader
{
    public static string? ReadUserInput(string caseDirectoryPath)
    {
        var readmePath = Path.Combine(caseDirectoryPath, "README.md");
        if (!File.Exists(readmePath))
        {
            return null;
        }

        var text = File.ReadAllText(readmePath);
        var match = UserInputSectionPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["content"].Value.Trim();
    }

    public static string BuildPlanningRequestSummary(string userInput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);

        var sku = ExtractSku(userInput);
        var store = ExtractStore(userInput);

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("PLANNING REQUEST");
        builder.AppendLine($"User Input: {userInput.Trim()}");

        if (!string.IsNullOrWhiteSpace(sku))
        {
            builder.AppendLine($"Product Scope: {sku}");
        }

        if (!string.IsNullOrWhiteSpace(store))
        {
            builder.AppendLine($"Locations: {store}");
        }

        return builder.ToString().Trim();
    }

    private static string? ExtractSku(string text)
    {
        var match = SkuPattern().Match(text);
        return match.Success ? match.Groups["sku"].Value : null;
    }

    private static string? ExtractStore(string text)
    {
        var match = StorePattern().Match(text);
        return match.Success ? match.Groups["store"].Value : null;
    }

    [GeneratedRegex(@"###\s*User Input\s*\r?\n+(?<content>.*?)(?:\r?\n###\s|\z)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UserInputSectionPattern();

    [GeneratedRegex(@"\bSKU\s+(?<sku>[A-Z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SkuPattern();

    [GeneratedRegex(@"\bstore\s+(?<store>[A-Z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex StorePattern();
}
