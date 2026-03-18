namespace DocAgent.McpServer.Cli;

public static class CliRunner
{
    private static readonly HashSet<string> KnownSubcommands =
        new(StringComparer.OrdinalIgnoreCase) { "install", "init", "update", "hooks" };

    public static bool IsCliCommand(string[]? args) =>
        args is { Length: > 0 } && KnownSubcommands.Contains(args[0]);

    public static async Task<int> RunAsync(string[] args)
    {
        return args[0].ToLowerInvariant() switch
        {
            "install" => await InstallCommand.RunAsync(args[1..]),
            "init"    => await InitCommand.RunAsync(args[1..]),
            "update"  => await UpdateCommand.RunAsync(args[1..]),
            "hooks"   => await HooksCommand.RunAsync(args[1..]),
            _         => 1
        };
    }
}
