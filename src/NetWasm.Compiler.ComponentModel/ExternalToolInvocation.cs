using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ExternalToolInvocation(
    string Executable,
    ImmutableArray<string> Arguments,
    ImmutableArray<string> EnvironmentVariablePrefixesToRemove);
