using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// An entry readies the runtime by calling the module's one runtime initializer.
internal sealed class RuntimeStateInitializer : IRuntimeStateInitializer
{
    public void Initialize(
        GeneratedFunctionWriterLease code,
        RuntimeInitializationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(plan);
        var index = plan.InitializerFunctionIndex ?? throw new InvalidOperationException(
            "Runtime initialization requires the module's initializer function.");
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)index)));
    }
}
