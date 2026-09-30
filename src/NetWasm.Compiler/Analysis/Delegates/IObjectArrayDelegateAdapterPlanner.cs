using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Analysis.Delegates;

internal interface IObjectArrayDelegateAdapterPlanner
{
    ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan> Plan(
        IEnumerable<MethodInstanceModel> reachableMethods);
}
