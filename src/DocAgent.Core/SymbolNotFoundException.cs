namespace DocAgent.Core;

/// <summary>Thrown when a symbol ID does not exist in the graph.</summary>
public sealed class SymbolNotFoundException : Exception
{
    public SymbolId SymbolId { get; }

    public SymbolNotFoundException(SymbolId symbolId)
        : base($"Symbol '{symbolId.Value}' not found in the graph.")
    {
        SymbolId = symbolId;
    }

    public SymbolNotFoundException(SymbolId symbolId, string message)
        : base(message)
    {
        SymbolId = symbolId;
    }

    public SymbolNotFoundException(SymbolId symbolId, string message, Exception innerException)
        : base(message, innerException)
    {
        SymbolId = symbolId;
    }
}
