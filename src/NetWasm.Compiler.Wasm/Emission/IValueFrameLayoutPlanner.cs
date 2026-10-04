using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IValueFrameLayoutPlanner
{
    ValueFrameLayout Create(StructuredMethodHeader header, NativeImportPlan? nativeImports = null,
        MemberExecutionPlan? memberExecution = null);
}
