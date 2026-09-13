using NetWasm.Compiler.Wasm.Encoding;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class FunctionLoadEmitter(
    ITargetLayout layouts,
    IManagedCallSiteResolver callSites,
    IInstructionCommandFactory commands) :
    InstructionCommandProvider,
    IFunctionLoader
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        commands.Create(
            CilOperation.LoadFunction,
            InstructionFamily.CallsAndCallableLoading,
            EmitLoad),
    ];

    public void Load(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices) => EmitLoad(request, code, functionIndices);

    private void EmitLoad(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var target = callSites.Resolve(request).Target;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(functionIndices.Resolve(target))));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            request.Stack.Count,
            CliValueKind.NativeInt)))));
        request.Stack.Add(CliValueKind.NativeInt);
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
