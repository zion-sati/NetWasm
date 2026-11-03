using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal interface IManagedMethodInliner
{
    CilMethodBody Inline(
        MethodInstanceModel caller,
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks,
        bool nested = false,
        bool smallOnly = false);
}
