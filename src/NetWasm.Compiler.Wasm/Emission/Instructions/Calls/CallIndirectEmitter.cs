using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class CallIndirectEmitter(
    ITargetLayout layouts,
    IImplicitExceptionEmitter exceptions) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new InstructionCommand(
            CilOperation.CallIndirect,
            InstructionFamily.CallsAndCallableLoading,
            EmitCallIndirect),
    ];

    private void EmitCallIndirect(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var signature = request.Instruction.Operand is CilOperand.CallSite callSite
            ? callSite.Signature
            : throw new InvalidOperationException("calli has no call-site signature");
        if (signature.ReturnSignatureType.StackKind == CliValueKind.ValueType)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedCil,
                "calli with a value-type return is not yet supported"));
        }
        var argumentBase = request.Stack.Count - signature.ParameterTypes.Length - 1;
        var functionSlot = argumentBase + signature.ParameterTypes.Length;
        var compatible = request.Target.CallableMethods.Values
            .Where(target => target.Definition.IsStatic &&
                DelegateSignatureComparer.AreEqual(target.Signature, signature))
            .OrderBy(target => target.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        foreach (var target in compatible)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                functionSlot,
                CliValueKind.NativeInt)))));
            EmitAddressConstant(
                code,
                functionIndices.Resolve(target));
            if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Equal));
            else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            for (var index = 0; index < signature.ParameterTypes.Length; index++)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                    request.Context,
                    argumentBase + index,
                    request.Stack[argumentBase + index])))));
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(target)))));
            if (signature.ReturnType != CliValueKind.Void)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                    request.Context,
                    argumentBase,
                    signature.ReturnType)))));
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        for (var index = 0; index < compatible.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        request.Stack.RemoveRange(argumentBase, signature.ParameterTypes.Length + 1);
        if (signature.ReturnType != CliValueKind.Void)
        {
            request.Stack.Add(signature.ReturnType);
        }
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        else code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
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
