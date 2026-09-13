using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Analysis.Delegates;

internal interface IDelegateBindingPlanner
{
    ImmutableArray<ManagedDelegateBinding> Plan(
        IEnumerable<MethodInstanceModel> invokes,
        IEnumerable<MethodInstanceModel> targets);
}
