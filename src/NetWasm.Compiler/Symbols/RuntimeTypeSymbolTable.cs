using System.Collections.Immutable;

namespace NetWasm.Compiler.Symbols;

// Every runtime type's names, ordered by type ID; the exception types among them, which every build's
// type-symbols file names for crash reports; and the digest of all of them, which goes into the build ID.
public sealed record RuntimeTypeSymbolTable(
    ImmutableArray<RuntimeTypeSymbol> Symbols,
    ImmutableHashSet<int> ExceptionTypeIds,
    ImmutableArray<byte> Digest);
