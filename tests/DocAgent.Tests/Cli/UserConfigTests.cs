using System.Text.Json;
using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class UserConfigTests : IDisposable
{
    private readonly string _tempDir;

    public UserConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new UserConfig
        {
            InstalledAt = DateTimeOffset.UtcNow
        };

        config.Version.Should().Be(1);
        config.HostingMode.Should().Be(HostingMode.A);
        config.BinaryPath.Should().BeNull();
        config.SourceProjectPath.Should().BeNull();
        config.ToolVersion.Should().BeNull();
    }

    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        var installedAt = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var config = new UserConfig
        {
            Version = 1,
            HostingMode = HostingMode.B,
            BinaryPath = "/usr/local/bin/docagent",
            SourceProjectPath = "/home/user/projects/docagent",
            ArtifactsDir = "/home/user/.docagent/artifacts",
            InstalledAt = installedAt,
            ToolVersion = "2.1.0"
        };

        var json = JsonSerializer.Serialize(config, UserConfig.JsonOptions);
        var deserialized = JsonSerializer.Deserialize<UserConfig>(json, UserConfig.JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be(1);
        deserialized.HostingMode.Should().Be(HostingMode.B);
        deserialized.BinaryPath.Should().Be("/usr/local/bin/docagent");
        deserialized.SourceProjectPath.Should().Be("/home/user/projects/docagent");
        deserialized.ArtifactsDir.Should().Be("/home/user/.docagent/artifacts");
        deserialized.InstalledAt.Should().Be(installedAt);
        deserialized.ToolVersion.Should().Be("2.1.0");
    }

    [Fact]
    public async Task JsonPropertyNames_AreCamelCase()
    {
        var config = new UserConfig
        {
            HostingMode = HostingMode.C,
            InstalledAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(config, UserConfig.JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("version", out _).Should().BeTrue("version should be camelCase");
        root.TryGetProperty("hostingMode", out _).Should().BeTrue("hostingMode should be camelCase");
        root.TryGetProperty("binaryPath", out _).Should().BeTrue("binaryPath should be camelCase");
        root.TryGetProperty("sourceProjectPath", out _).Should().BeTrue("sourceProjectPath should be camelCase");
        root.TryGetProperty("artifactsDir", out _).Should().BeTrue("artifactsDir should be camelCase");
        root.TryGetProperty("installedAt", out _).Should().BeTrue("installedAt should be camelCase");
        root.TryGetProperty("toolVersion", out _).Should().BeTrue("toolVersion should be camelCase");
    }

    [Fact]
    public async Task HostingMode_SerializesAsString()
    {
        var config = new UserConfig
        {
            HostingMode = HostingMode.B,
            InstalledAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(config, UserConfig.JsonOptions);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("hostingMode").GetString().Should().Be("B");
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var installedAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var config = new UserConfig
        {
            HostingMode = HostingMode.A,
            BinaryPath = "/usr/bin/docagent",
            InstalledAt = installedAt,
            ToolVersion = "1.0.0"
        };

        var path = Path.Combine(_tempDir, "config.json");
        await UserConfig.SaveAsync(config, path);

        File.Exists(path).Should().BeTrue();

        var loaded = await UserConfig.LoadAsync(path);
        loaded.Should().NotBeNull();
        loaded!.HostingMode.Should().Be(HostingMode.A);
        loaded.BinaryPath.Should().Be("/usr/bin/docagent");
        loaded.InstalledAt.Should().Be(installedAt);
        loaded.ToolVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileNotFound()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.json");

        var result = await UserConfig.LoadAsync(path);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenJsonIsMalformed()
    {
        var path = Path.Combine(_tempDir, "malformed.json");
        await File.WriteAllTextAsync(path, "{ this is not valid json !!!");

        var result = await UserConfig.LoadAsync(path);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoriesAsNeeded()
    {
        var deepPath = Path.Combine(_tempDir, "deep", "nested", "config.json");

        var config = new UserConfig { InstalledAt = DateTimeOffset.UtcNow };
        await UserConfig.SaveAsync(config, deepPath);

        File.Exists(deepPath).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_WithNullPath_UsesDefaultConfigPath()
    {
        // This test just verifies that LoadAsync(null) doesn't throw and returns
        // null when the default config file doesn't exist (most CI environments).
        // We don't create the file, so we expect null back.
        // Note: We cannot reliably control ~/.docagent/config.json in tests.
        var defaultPath = UserConfig.DefaultConfigPath;
        if (!File.Exists(defaultPath))
        {
            var result = await UserConfig.LoadAsync(null);
            result.Should().BeNull();
        }
    }

    [Fact]
    public void DefaultDocAgentDir_IsUnderUserHome()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        UserConfig.DefaultDocAgentDir.Should().StartWith(homeDir);
    }

    [Fact]
    public void DefaultConfigPath_IsInsideDefaultDocAgentDir()
    {
        UserConfig.DefaultConfigPath.Should().StartWith(UserConfig.DefaultDocAgentDir);
    }

    [Fact]
    public async Task AllHostingModes_RoundTripCorrectly()
    {
        foreach (var mode in Enum.GetValues<HostingMode>())
        {
            var config = new UserConfig { HostingMode = mode, InstalledAt = DateTimeOffset.UtcNow };
            var json = JsonSerializer.Serialize(config, UserConfig.JsonOptions);
            var deserialized = JsonSerializer.Deserialize<UserConfig>(json, UserConfig.JsonOptions);
            deserialized!.HostingMode.Should().Be(mode, $"mode {mode} should round-trip");
        }
    }
}
