using System.Text.Json;
using CohereInventoryAndTrend.AgentProvisioning.Models;

namespace CohereInventoryAndTrend.AgentProvisioning;

public sealed class AgentAssetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _agentsRoot;

    public AgentAssetLoader(string agentsRoot)
    {
        _agentsRoot = Path.GetFullPath(agentsRoot);
        if (!Directory.Exists(_agentsRoot))
        {
            throw new InvalidOperationException($"Agents directory was not found at '{_agentsRoot}'.");
        }
    }

    public IReadOnlyList<AgentAssetBundle> LoadAll()
    {
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        List<AgentAssetBundle> bundles = [];

        foreach (string agentDirectory in Directory
                     .EnumerateDirectories(_agentsRoot)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            AgentAssetBundle bundle = LoadAgentDirectory(agentDirectory);
            if (!seenNames.Add(bundle.Manifest.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate agent name '{bundle.Manifest.Name}' found in '{agentDirectory}'.");
            }

            bundles.Add(bundle);
        }

        if (bundles.Count == 0)
        {
            throw new InvalidOperationException($"No agent asset folders were found in '{_agentsRoot}'.");
        }

        return bundles;
    }

    private AgentAssetBundle LoadAgentDirectory(string agentDirectory)
    {
        string manifestPath = Path.Combine(agentDirectory, "agent.json");
        string instructionsPath = Path.Combine(agentDirectory, "instructions.md");
        string mcpPath = Path.Combine(agentDirectory, "mcp.json");

        EnsureFileExists(manifestPath);
        EnsureFileExists(instructionsPath);
        EnsureFileExists(mcpPath);

        AgentManifest manifest = DeserializeRequired<AgentManifest>(manifestPath);
        McpDeclaration mcp = DeserializeRequired<McpDeclaration>(mcpPath);

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException($"Agent manifest in '{manifestPath}' is missing name.");
        }

        if (manifest.AllowedDecisions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{manifest.Name}' must declare at least one allowed decision value.");
        }

        string instructions = File.ReadAllText(instructionsPath).Trim();
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new InvalidOperationException($"Agent '{manifest.Name}' instructions file is empty.");
        }

        string outputSchemaPath = ResolvePath(agentDirectory, manifest.OutputSchemaFile);
        EnsureFileExists(outputSchemaPath);
        string outputSchemaJson = File.ReadAllText(outputSchemaPath).Trim();
        if (string.IsNullOrWhiteSpace(outputSchemaJson))
        {
            throw new InvalidOperationException(
                $"Agent '{manifest.Name}' output schema file '{outputSchemaPath}' is empty.");
        }

        ValidateJsonObject(outputSchemaPath, outputSchemaJson);

        return new AgentAssetBundle
        {
            Manifest = manifest,
            Instructions = instructions,
            OutputSchemaJson = outputSchemaJson,
            Mcp = mcp
        };
    }

    public static string ResolveAgentsRoot(string? agentsRoot)
    {
        if (!string.IsNullOrWhiteSpace(agentsRoot))
        {
            return Path.GetFullPath(agentsRoot);
        }

        string outputAgentsRoot = Path.Combine(AppContext.BaseDirectory, "agents");
        if (Directory.Exists(outputAgentsRoot))
        {
            return outputAgentsRoot;
        }

        string projectAgentsRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "agents"));

        if (Directory.Exists(projectAgentsRoot))
        {
            return projectAgentsRoot;
        }

        return outputAgentsRoot;
    }

    private static T DeserializeRequired<T>(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                ?? throw new InvalidOperationException($"File '{path}' could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"File '{path}' contains invalid JSON.", ex);
        }
    }

    private static void ValidateJsonObject(string path, string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"File '{path}' must contain a JSON object.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"File '{path}' contains invalid JSON.", ex);
        }
    }

    private static string ResolvePath(string baseDirectory, string relativePath)
    {
        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: '{path}'.");
        }
    }
}
