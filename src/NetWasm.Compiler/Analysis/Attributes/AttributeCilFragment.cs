using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed record AttributeCilFragment(
    ImmutableArray<CilInstruction> Instructions,
    ImmutableArray<CliTypeIdentity> LocalTypes,
    int MaxStack);
