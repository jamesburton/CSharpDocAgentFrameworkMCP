namespace DocAgent.McpServer.Cli;

public static class HooksCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("hooks command not yet implemented");
        return Task.FromResult(1);
    }
}
