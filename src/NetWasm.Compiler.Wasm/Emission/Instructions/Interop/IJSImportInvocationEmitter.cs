using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// Arguments occupy evaluation-stack slots [ArgumentBase, ArgumentBase + Consumed); the
// marshalled result replaces them at ArgumentBase. A value-type result is held in
// the value-frame temporary of the call at Offset.
internal sealed record JSImportInvocation(
    MethodInstanceModel Method,
    MethodEmissionContext Context,
    List<CliValueKind> Stack,
    int ArgumentBase,
    int Consumed,
    InstructionModuleTarget Target,
    int Offset);

internal interface IJSImportInvocationEmitter
{
    void Emit(
        JSImportInvocation invocation,
        IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices);
}
