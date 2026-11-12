using System.Collections.Immutable;

namespace NetWasm.Compiler.Symbols;

public enum SymbolSidecarKind
{
    // <program>.wasm.type-symbols: per runtime type ID, its name, namespace, full name and display text.
    Type = 1,
    // <program>.wasm.method-symbols: per stack-trace symbol ID, its frame text.
    Method = 2,
}

// One symbols sidecar's bytes (format: SymbolSidecarWriter).
public sealed record SymbolSidecarArtifact(SymbolSidecarKind Kind, byte[] Bytes)
{
    public const string TypeSymbolsMediaType = "application/vnd.netwasm.type-symbols;version=1";
    public const string MethodSymbolsMediaType = "application/vnd.netwasm.method-symbols;version=1";

    public string MediaType => Kind == SymbolSidecarKind.Type ? TypeSymbolsMediaType : MethodSymbolsMediaType;
}

// One entry: its ID and its strings (null where it has none, such as a type without a namespace).
public sealed record SymbolSidecarEntry(int Id, ImmutableArray<string?> Strings);
