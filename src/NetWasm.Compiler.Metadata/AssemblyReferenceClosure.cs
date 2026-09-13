using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record AssemblyReferenceClosure(
    AssemblyIdentity Identity,
    ImmutableArray<string> References);
