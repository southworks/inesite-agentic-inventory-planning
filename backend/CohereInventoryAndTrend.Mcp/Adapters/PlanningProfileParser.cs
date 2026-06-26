using System.Globalization;
using CohereInventoryAndTrend.Mcp.Models;

namespace CohereInventoryAndTrend.Mcp.Adapters;

public static class PlanningProfileParser
{
    public static PlanningProfile Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new PlanningProfile
        {
            Category = ReadLabel(text, "Category"),
            Campaign = ReadLabel(text, "Campaign"),
            PlanningHorizon = ReadLabel(text, "Planning Horizon"),
            Locations = ReadLabel(text, "Locations"),
            ProductScope = ReadLabel(text, "Product Scope"),
            BudgetLimit = ReadDecimal(text, "Budget Limit"),
            TargetServiceLevel = ReadDecimal(text, "Target Service Level")
        };
    }

    public static bool LooksLikePlanningRequestDocument(string text) =>
        text.Contains("PLANNING REQUEST", StringComparison.OrdinalIgnoreCase)
        || text.Contains("planning_request", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Product Scope", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Campaign", StringComparison.OrdinalIgnoreCase);

    private static string? ReadLabel(string text, string label)
    {
        foreach (string line in text.Split('\n', StringSplitOptions.TrimEntries))
        {
            int separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            string lineLabel = line[..separatorIndex].Trim();
            if (!lineLabel.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(separatorIndex + 1)..].Trim();
        }

        return null;
    }

    private static decimal? ReadDecimal(string text, string label)
    {
        string? raw = ReadLabel(text, label);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string normalized = raw
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }
}
