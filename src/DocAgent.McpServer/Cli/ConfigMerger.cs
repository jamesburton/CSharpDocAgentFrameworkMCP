using System.Text.Json;
using System.Text.Json.Nodes;

namespace DocAgent.McpServer.Cli;

/// <summary>Outcome of a <see cref="ConfigMerger.MergeAsync"/> call.</summary>
public enum MergeConflict
{
    /// <summary>Entry was written (new file, new key, or idempotent no-op).</summary>
    None,

    /// <summary>An existing, different entry was found and the merge was skipped.</summary>
    Skipped,

    /// <summary>An existing, different entry was found and was overwritten.</summary>
    Overwritten
}

/// <summary>
/// Reads, merges, and writes agent config JSON files for MCP server registration.
/// </summary>
public static class ConfigMerger
{
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Merges a <c>docagent</c> entry into the config file at <paramref name="configPath"/>.
    /// </summary>
    /// <param name="configPath">Absolute path to the config file. Created if absent.</param>
    /// <param name="keyPath">
    /// Dot-separated path to the server map inside the JSON document
    /// (e.g. <c>"mcpServers"</c> or <c>"mcp.servers"</c>).
    /// </param>
    /// <param name="mcpEntry">The <see cref="JsonObject"/> to write as <c>docagent</c>.</param>
    /// <param name="nonInteractive">
    /// When <see langword="true"/>, the method never prompts the user.
    /// </param>
    /// <param name="yesFlag">
    /// When <see langword="true"/> (and <paramref name="nonInteractive"/> is also
    /// <see langword="true"/>), conflicting entries are overwritten silently.
    /// </param>
    /// <returns>
    /// <see cref="MergeConflict.None"/> if the file was written or the entry was already
    /// identical; <see cref="MergeConflict.Skipped"/> if a conflicting entry was found and
    /// the user chose not to overwrite; <see cref="MergeConflict.Overwritten"/> if the
    /// conflicting entry was replaced.
    /// </returns>
    public static async Task<MergeConflict> MergeAsync(
        string configPath,
        string keyPath,
        JsonObject mcpEntry,
        bool nonInteractive,
        bool yesFlag = false)
    {
        // Load or bootstrap the document
        JsonNode root = await LoadOrCreateRootAsync(configPath);

        // Navigate / create the server-map node
        JsonObject serverMap = NavigateOrCreate(root, keyPath);

        // Serialise the new entry for comparison
        string newEntryJson = mcpEntry.ToJsonString();

        // Check for an existing docagent entry
        if (serverMap.TryGetPropertyValue("docagent", out JsonNode? existing) && existing is not null)
        {
            string existingJson = existing.ToJsonString();

            // Idempotent no-op: entry is already identical
            if (existingJson == newEntryJson)
                return MergeConflict.None;

            // Conflict handling
            if (nonInteractive)
            {
                if (!yesFlag)
                {
                    await Console.Error.WriteLineAsync(
                        $"[docagent] Conflict: 'docagent' already exists in {configPath} with a different value. " +
                        "Re-run with --yes to overwrite.");
                    return MergeConflict.Skipped;
                }

                // yesFlag=true → fall through and overwrite
            }
            else
            {
                // Interactive: prompt the user
                Console.Write($"[docagent] 'docagent' already exists in {configPath}. Overwrite? [y/N] ");
                var answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    return MergeConflict.Skipped;
                }
            }

            // Remove the old entry before writing the new one
            serverMap.Remove("docagent");

            // Write the updated document
            serverMap["docagent"] = JsonNode.Parse(newEntryJson);
            await WriteRootAsync(configPath, root);
            return MergeConflict.Overwritten;
        }

        // No existing entry — add it
        serverMap["docagent"] = JsonNode.Parse(newEntryJson);
        await WriteRootAsync(configPath, root);
        return MergeConflict.None;
    }

    /// <summary>
    /// Builds the JSON object that represents the DocAgent MCP server entry.
    /// </summary>
    /// <param name="mode">Hosting mode that determines the launch command.</param>
    /// <param name="binaryPath">
    /// Absolute path to the pre-built binary (Mode B only).
    /// </param>
    /// <param name="sourcePath">
    /// Absolute path to the source project for <c>dotnet run</c> (Mode C only).
    /// </param>
    /// <param name="artifactsDir">Absolute path to the artifacts directory (all modes).</param>
    public static JsonObject BuildMcpEntry(
        HostingMode mode,
        string? binaryPath = null,
        string? sourcePath = null,
        string? artifactsDir = null)
    {
        var resolvedArtifactsDir = artifactsDir
            ?? Path.Combine(UserConfig.DefaultDocAgentDir, "artifacts");

        var env = new JsonObject
        {
            ["DOCAGENT_ARTIFACTS_DIR"] = JsonValue.Create(resolvedArtifactsDir)
        };

        return mode switch
        {
            HostingMode.A => new JsonObject
            {
                ["command"] = JsonValue.Create("docagent"),
                ["args"] = new JsonArray(),
                ["env"] = env
            },

            HostingMode.B => new JsonObject
            {
                ["command"] = JsonValue.Create(binaryPath
                    ?? throw new ArgumentNullException(nameof(binaryPath), "binaryPath is required for Mode B")),
                ["args"] = new JsonArray(),
                ["env"] = env
            },

            HostingMode.C => new JsonObject
            {
                ["command"] = JsonValue.Create("dotnet"),
                ["args"] = new JsonArray(
                    JsonValue.Create("run"),
                    JsonValue.Create("--project"),
                    JsonValue.Create(sourcePath
                        ?? throw new ArgumentNullException(nameof(sourcePath), "sourcePath is required for Mode C"))),
                ["env"] = env
            },

            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown hosting mode")
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<JsonNode> LoadOrCreateRootAsync(string configPath)
    {
        if (!File.Exists(configPath))
            return new JsonObject();

        var text = await File.ReadAllTextAsync(configPath);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException)
        {
            // Treat unparsable files as empty
            return new JsonObject();
        }
    }

    /// <summary>
    /// Navigates the dot-separated <paramref name="keyPath"/> in <paramref name="root"/>,
    /// creating intermediate <see cref="JsonObject"/> nodes as needed, and returns the
    /// innermost node as a <see cref="JsonObject"/>.
    /// </summary>
    private static JsonObject NavigateOrCreate(JsonNode root, string keyPath)
    {
        var parts = keyPath.Split('.');
        JsonObject current = root.AsObject();

        foreach (var part in parts)
        {
            if (!current.TryGetPropertyValue(part, out JsonNode? child) || child is null)
            {
                var newObj = new JsonObject();
                current[part] = newObj;
                current = newObj;
            }
            else if (child is JsonObject obj)
            {
                current = obj;
            }
            else
            {
                // The existing node is a scalar/array — replace with an object
                var newObj = new JsonObject();
                current[part] = newObj;
                current = newObj;
            }
        }

        return current;
    }

    private static async Task WriteRootAsync(string configPath, JsonNode root)
    {
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = root.ToJsonString(s_writeOptions);
        await File.WriteAllTextAsync(configPath, json);
    }
}
