namespace DocAgent.McpServer.Cli;

public static class InstallCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("install command not yet implemented");
        return Task.FromResult(1);
    }
}
