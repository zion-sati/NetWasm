using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

namespace NetWasm.Compiler.Analysis;

internal interface IMemberExecutionPlanner
{
    MemberExecutionPlan Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<MethodInstanceModel> methodDescriptors,
        IEnumerable<FieldInstanceModel> fieldDescriptors);
}
