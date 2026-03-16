namespace DocAgent.McpServer.Cli;

public static class UpdateCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("update command not yet implemented");
        return Task.FromResult(1);
    }
}
