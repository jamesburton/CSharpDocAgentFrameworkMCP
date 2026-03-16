namespace DocAgent.McpServer.Cli;

public static class InitCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("init command not yet implemented");
        return Task.FromResult(1);
    }
}
