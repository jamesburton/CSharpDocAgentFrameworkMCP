using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocAgent.McpServer.Cli;

/// <summary>Represents the schema for a <c>docagent.project.json</c> project configuration file.</summary>
public sealed class ProjectConfig
{
    /// <summary>Default filename used when loading from a directory.</summary>
    public const string DefaultFileName = "docagent.project.json";

    /// <summary>Shared <see cref="JsonSerializerOptions"/> for consistent camelCase serialization.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Schema version. Always 1 for this release.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Path to the primary .sln, .slnx, or .csproj file.</summary>
    [JsonPropertyName("primarySource")]
    public string PrimarySource { get; set; } = string.Empty;

    /// <summary>Optional secondary sources (NuGet packages, local projects, etc.).</summary>
    [JsonPropertyName("secondarySources")]
    public List<SecondarySource> SecondarySources { get; set; } = [];

    /// <summary>Directory where snapshot and index artifacts are stored.</summary>
    [JsonPropertyName("artifactsDir")]
    public string ArtifactsDir { get; set; } = ".docagent/artifacts";

    /// <summary>When <see langword="true"/>, test project files are excluded during ingestion.</summary>
    [JsonPropertyName("excludeTestFiles")]
    public bool ExcludeTestFiles { get; set; } = true;

    /// <summary>
    /// Loads a <see cref="ProjectConfig"/> from <paramref name="path"/>.
    /// Returns <see langword="null"/> if the file does not exist.
    /// </summary>
    public static async Task<ProjectConfig?> LoadAsync(string path)
    {
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProjectConfig>(stream, JsonOptions);
    }

    /// <summary>
    /// Saves <paramref name="config"/> to <paramref name="path"/>, creating directories as needed.
    /// </summary>
    public static async Task SaveAsync(ProjectConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }

    /// <summary>
    /// Convenience method that loads <c>docagent.project.json</c> from <paramref name="dir"/>.
    /// Returns <see langword="null"/> if the file does not exist.
    /// </summary>
    public static Task<ProjectConfig?> LoadFromDirAsync(string dir)
    {
        var path = Path.Combine(dir, DefaultFileName);
        return LoadAsync(path);
    }
}

/// <summary>Describes a secondary documentation source (e.g. a NuGet package or local project).</summary>
public sealed class SecondarySource
{
    /// <summary>Source type identifier (e.g. <c>"nuget"</c>, <c>"local"</c>).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Path or package reference for this source.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
