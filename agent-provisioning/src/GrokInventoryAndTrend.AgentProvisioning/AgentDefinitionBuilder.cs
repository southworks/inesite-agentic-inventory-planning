using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrokInventoryAndTrend.AgentProvisioning.Models;

namespace GrokInventoryAndTrend.AgentProvisioning;

public sealed class AgentDefinitionBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public string BuildDefinitionJson(AgentAssetBundle bundle, ProvisioningSettings settings)
    {
        string serverUrl = $"{settings.McpBaseUrl.TrimEnd('/')}{NormalizePath(bundle.Mcp.Path)}";

        JsonObject definition = new()
        {
            ["kind"] = "prompt",
            ["model"] = settings.ModelDeploymentName,
            ["instructions"] = BuildInstructions(bundle),
            ["temperature"] = 0.2,
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "mcp",
                    ["server_label"] = bundle.Mcp.ServerLabel,
                    ["server_url"] = serverUrl,
                    ["require_approval"] = "never"
                }
            },
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "AgentStructuredOutput",
                    ["description"] = "Structured agent output consumed by the inventory planning workflow API.",
                    ["schema"] = JsonNode.Parse(bundle.OutputSchemaJson),
                    ["strict"] = true
                }
            }
        };

        return definition.ToJsonString(SerializerOptions);
    }

    public string BuildCreateVersionRequestJson(string definitionJson, AgentManifest manifest)
    {
        JsonObject request = new()
        {
            ["definition"] = JsonNode.Parse(definitionJson),
            ["description"] = manifest.Description
        };

        return request.ToJsonString(SerializerOptions);
    }

    public string ComputeFingerprint(string definitionJson)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(definitionJson));
        return Convert.ToHexString(hash);
    }

    private static string BuildInstructions(AgentAssetBundle bundle)
    {
        StringBuilder builder = new();
        builder.AppendLine(bundle.Instructions);
        builder.AppendLine();
        builder.AppendLine("## Structured Output Contract");
        if (UsesForecastingStructuredOutput(bundle))
        {
            builder.AppendLine("Return JSON only with these required properties: summary, decision, evidence, confidenceLevel, anomalies, keyMetrics.");
            builder.AppendLine("The API requires all of these properties.");
            builder.AppendLine();
            builder.AppendLine("Formatting rules:");
            builder.AppendLine("- Return raw JSON only. Do not wrap the JSON in markdown code fences.");
            builder.AppendLine("- Do not include extra text before or after the JSON.");
            builder.AppendLine("- Use valid JSON syntax. Numeric values must not include currency symbols such as $.");
            builder.AppendLine("- evidence must be a plain string, not an object or array.");
            builder.AppendLine("- confidenceLevel must be Low, Medium, or High.");
            builder.AppendLine("- anomalies and keyMetrics must be arrays of strings. Use empty arrays when none apply.");
        }
        else if (UsesPlannerCopilotStructuredOutput(bundle))
        {
            builder.AppendLine("Return JSON only with these required properties: summary, decision, evidence, approvalAssessment, budgetImpact, serviceLevelImpact, concerns, recommendations.");
            builder.AppendLine("The API requires all of these properties.");
            builder.AppendLine();
            builder.AppendLine("Formatting rules:");
            builder.AppendLine("- Return raw JSON only. Do not wrap the JSON in markdown code fences.");
            builder.AppendLine("- Do not include extra text before or after the JSON.");
            builder.AppendLine("- Use valid JSON syntax. Numeric values must not include currency symbols such as $.");
            builder.AppendLine("- evidence must be a plain string, not an object or array.");
            builder.AppendLine("- approvalAssessment must be Supported, Partially Supported, Not Supported, or Insufficient Information.");
            builder.AppendLine("- budgetImpact must be Within Budget, At Risk, Over Budget, or Unknown.");
            builder.AppendLine("- serviceLevelImpact must be Meets Target, At Risk, Below Target, or Unknown.");
            builder.AppendLine("- concerns and recommendations must be arrays of strings. Use empty arrays when none apply.");
        }
        else
        {
            builder.AppendLine("Return JSON only with these required properties: summary, decision, evidence.");
            builder.AppendLine("The API requires summary, decision, and evidence.");
            builder.AppendLine();
            builder.AppendLine("Formatting rules:");
            builder.AppendLine("- Return raw JSON only. Do not wrap the JSON in markdown code fences.");
            builder.AppendLine("- Do not include extra text before or after the JSON.");
            builder.AppendLine("- Use valid JSON syntax. Numeric values must not include currency symbols such as $.");
            builder.AppendLine("- evidence must be a plain string, not an object or array.");
        }
        builder.AppendLine();
        builder.AppendLine("## Allowed Decision Values");
        foreach (string decision in bundle.Manifest.AllowedDecisions)
        {
            builder.AppendLine($"- {decision}");
        }

        builder.AppendLine();
        builder.AppendLine("## Workflow Boundaries");
        builder.AppendLine("Consume prior workflow outputs when provided. Do not repeat work owned by earlier agents.");
        builder.AppendLine("Produce recommendations and evidence only. Human-in-the-loop orchestration is handled by the workflow, not by this agent.");

        return builder.ToString().Trim();
    }

    private static bool UsesForecastingStructuredOutput(AgentAssetBundle bundle) =>
        bundle.OutputSchemaJson.Contains("\"confidenceLevel\"", StringComparison.Ordinal);

    private static bool UsesPlannerCopilotStructuredOutput(AgentAssetBundle bundle) =>
        bundle.OutputSchemaJson.Contains("\"approvalAssessment\"", StringComparison.Ordinal);

    private static string NormalizePath(string path)
    {
        return path.StartsWith('/') ? path : $"/{path}";
    }
}
