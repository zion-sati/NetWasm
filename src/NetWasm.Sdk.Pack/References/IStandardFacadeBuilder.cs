namespace NetWasm.Sdk.Pack.References;

/// <summary>Builds a metadata-only forwarding contract from immutable assembly images.</summary>
public interface IStandardFacadeBuilder
{
    /// <summary>netstandard.dll: the .NET Standard contract's types forwarded to their owners.</summary>
    byte[] Build(byte[] contract, IReadOnlyList<byte[]> owners);

    /// <summary>
    /// A facade with any contract's identity (a .NET reference pack's System.Runtime or System.Buffers, whose types
    /// may themselves be forwarders): the contract's types its owners define, forwarded to them, and how many of the
    /// contract's types that is.
    /// </summary>
    FacadeImage BuildFacade(byte[] contract, IReadOnlyList<byte[]> owners);
}

/// <summary>A generated facade, the number of contract types it forwards and the contract's type count.</summary>
public sealed record FacadeImage(string Name, byte[] Image, int Forwarded, int ContractTypes);
