namespace NetWasm.Compiler.Symbols;

// A runtime type's names as .NET forms them, keyed by its runtime type ID. A program carries none of them
// (issue 225): they go to the type-symbols sidecar.
public sealed record RuntimeTypeSymbol(
    int TypeId,
    string Name,
    string? Namespace,
    string? FullName,
    string DisplayName);
