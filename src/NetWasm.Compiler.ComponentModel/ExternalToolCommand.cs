using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ExternalToolCommand(
    string Executable,
    ImmutableArray<string> ArgumentPrefix);
