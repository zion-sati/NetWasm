using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedMethodEmitter
{
    ManagedMethodEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        MethodInstanceModel? methodInstance,
        int stackTraceMethodId,
        RuntimeImportSelection runtimeImportSelection,
        Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody);
}
