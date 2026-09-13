using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class CallInstructionEmitter(
    IManagedCallSiteResolver callSites,
    ICallEmissionKindResolver kinds,
    ICallEmissionRegistry emitters) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.Call),
        Command(CilOperation.CallVirtual),
    ];

    private InstructionCommand Command(CilOperation operation) => new(
        operation,
        InstructionFamily.CallsAndCallableLoading,
        EmitCall);

    private void EmitCall(
        InstructionEmissionRequest instruction, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var callSite = callSites.Resolve(instruction);
        var consumed = callSite.Target.Signature.ParameterTypes.Length +
                       (callSite.Target.Definition.IsStatic ? 0 : 1);
        var request = new CallEmissionRequest(
            instruction,
            callSite.Target,
            instruction.Stack.Count - consumed,
            consumed,
            callSite.ConstrainedType);
        emitters.Get(kinds.Resolve(request, code)).Emit(request, code, functionIndices);
    }

}
