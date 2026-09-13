namespace NetWasm.Compiler.Diagnostics;

internal sealed record StructuredBlockOwnershipVisit(
    int BlockIndex,
    string Path,
    string? FirstPath);
