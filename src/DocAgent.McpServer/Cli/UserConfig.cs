using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocAgent.McpServer.Cli;

/// <summary>Hosting mode for the DocAgent MCP server.</summary>
public enum HostingMode
{
    /// <summary>Mode A: installed as a .NET global tool.</summary>
    A,

    /// <summary>Mode B: run from a pre-built binary.</summary>
    B,

    /// <summary>Mode C: run from source project.</summary>
    C
}

/// <summary>Represents the schema for the user-level <c>~/.docagent/config.json</c> configuration file.</summary>
public sealed class UserConfig
{
    /// <summary>Shared <see cref="JsonSerializerOptions"/> for consistent camelCase + string enum serialization.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    /// <summary>Default directory for user-level DocAgent configuration (<c>~/.docagent</c>).</summary>
    public static string DefaultDocAgentDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docagent");

    /// <summary>Default path for the user config file (<c>~/.docagent/config.json</c>).</summary>
    public static string DefaultConfigPath =>
        Path.Combine(DefaultDocAgentDir, "config.json");

    /// <summary>Schema version. Always 1 for this release.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Hosting mode that determines how DocAgent is launched.</summary>
    [JsonPropertyName("hostingMode")]
    public HostingMode HostingMode { get; set; } = HostingMode.A;

    /// <summary>Absolute path to the pre-built DocAgent binary (Mode B only).</summary>
    [JsonPropertyName("binaryPath")]
    public string? BinaryPath { get; set; }

    /// <summary>Absolute path to the DocAgent source project for <c>dotnet run</c> (Mode C only).</summary>
    [JsonPropertyName("sourceProjectPath")]
    public string? SourceProjectPath { get; set; }

    /// <summary>Directory where user-level artifacts are stored.</summary>
    [JsonPropertyName("artifactsDir")]
    public string ArtifactsDir { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".docagent",
            "artifacts");

    /// <summary>Timestamp when DocAgent was installed/configured.</summary>
    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; set; }

    /// <summary>Version of the DocAgent tool that wrote this file.</summary>
    [JsonPropertyName("toolVersion")]
    public string? ToolVersion { get; set; }

    /// <summary>
    /// Loads a <see cref="UserConfig"/> from <paramref name="path"/> (or <see cref="DefaultConfigPath"/> when
    /// <paramref name="path"/> is <see langword="null"/>).
    /// Returns <see langword="null"/> if the file does not exist or contains malformed JSON.
    /// </summary>
    public static async Task<UserConfig?> LoadAsync(string? path = null)
    {
        var resolvedPath = path ?? DefaultConfigPath;

        if (!File.Exists(resolvedPath))
            return null;

        try
        {
            await using var stream = File.OpenRead(resolvedPath);
            return await JsonSerializer.DeserializeAsync<UserConfig>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Saves <paramref name="config"/> to <paramref name="path"/> (or <see cref="DefaultConfigPath"/> when
    /// <paramref name="path"/> is <see langword="null"/>), creating directories as needed.
    /// </summary>
    public static async Task SaveAsync(UserConfig config, string? path = null)
    {
        var resolvedPath = path ?? DefaultConfigPath;
        var dir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(resolvedPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }
}
