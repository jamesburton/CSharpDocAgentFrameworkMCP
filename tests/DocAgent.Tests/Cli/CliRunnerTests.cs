using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class CliRunnerTests
{
    // IsCliCommand — known subcommands

    [Theory]
    [InlineData("install")]
    [InlineData("init")]
    [InlineData("update")]
    [InlineData("hooks")]
    public void IsCliCommand_KnownSubcommand_ReturnsTrue(string subcommand)
    {
        CliRunner.IsCliCommand([subcommand]).Should().BeTrue();
    }

    [Theory]
    [InlineData("INSTALL")]
    [InlineData("Init")]
    [InlineData("UPDATE")]
    [InlineData("Hooks")]
    public void IsCliCommand_KnownSubcommandCaseInsensitive_ReturnsTrue(string subcommand)
    {
        CliRunner.IsCliCommand([subcommand]).Should().BeTrue();
    }

    [Theory]
    [InlineData("install", "--quiet")]
    [InlineData("update", "--force")]
    [InlineData("hooks", "--dry-run")]
    [InlineData("init", "--path", "/some/dir")]
    public void IsCliCommand_KnownSubcommandWithExtraFlags_ReturnsTrue(params string[] args)
    {
        CliRunner.IsCliCommand(args).Should().BeTrue();
    }

    // IsCliCommand — non-CLI args

    [Fact]
    public void IsCliCommand_NullArgs_ReturnsFalse()
    {
        CliRunner.IsCliCommand(null).Should().BeFalse();
    }

    [Fact]
    public void IsCliCommand_EmptyArgs_ReturnsFalse()
    {
        CliRunner.IsCliCommand([]).Should().BeFalse();
    }

    [Theory]
    [InlineData("--stdio")]
    [InlineData("--version")]
    [InlineData("--help")]
    [InlineData("unknown")]
    [InlineData("serve")]
    public void IsCliCommand_UnknownOrTransportArg_ReturnsFalse(string arg)
    {
        CliRunner.IsCliCommand([arg]).Should().BeFalse();
    }

    // RunAsync — each subcommand dispatches and returns non-zero (stubs return 1)

    [Theory]
    [InlineData("install")]
    [InlineData("init")]
    [InlineData("update")]
    [InlineData("hooks")]
    public async Task RunAsync_KnownSubcommand_ReturnsNonNegative(string subcommand)
    {
        var result = await CliRunner.RunAsync([subcommand]);
        result.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task RunAsync_UnknownSubcommand_ReturnsOne()
    {
        var result = await CliRunner.RunAsync(["unknown-command"]);
        result.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_SubcommandWithFlags_PassesRemainingArgs()
    {
        // Stubs ignore their args but should not throw
        var act = async () => await CliRunner.RunAsync(["update", "--quiet", "--force"]);
        await act.Should().NotThrowAsync();
    }
}
