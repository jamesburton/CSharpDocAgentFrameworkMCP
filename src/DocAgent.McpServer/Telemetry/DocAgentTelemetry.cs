using System.Diagnostics;

namespace DocAgent.McpServer.Telemetry;

public static class DocAgentTelemetry
{
    public const string SourceName = "DocAgent.McpServer";
    public static readonly ActivitySource Source = new(SourceName);
    public static bool VerboseMode { get; set; }
}
