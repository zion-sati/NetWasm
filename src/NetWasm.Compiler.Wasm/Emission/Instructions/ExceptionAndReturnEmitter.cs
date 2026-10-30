using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class ExceptionAndReturnEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IMethodFrameExitEmitter methodFrameExits,
    IFilterEnvironmentRootEmitter filterEnvironmentRoots,
    IAddressInstructionEmitter addresses,
    IValueCopyEmitter valueCopies) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.Throw, EmitThrow),
        Command(CilOperation.Rethrow, EmitRethrow),
        Command(CilOperation.EndFilter, EmitEndFilter),
        Command(CilOperation.EndFinally, EmitEndFinally),
        Command(CilOperation.Return, EmitReturn),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.ExceptionsAndRoots,
        emit);

    private static void EmitEndFinally(InstructionEmissionRequest request, IWasmInstructionWriter code) =>
        ArgumentNullException.ThrowIfNull(request);

    private void EmitThrow(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var exceptionLocal = GetStackLocal(
            request.Context,
            request.Stack.Count - 1,
            CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(exceptionLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        var throwFunction = request.Context.ManagedThrowFunction;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        filterEnvironmentRoots.Emit(code, request.Context);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(exceptionLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)throwFunction.Value)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        request.Stack.Clear();
    }

    private void EmitRethrow(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var slot = request.Context.ActiveCatchRootSlot ?? throw new InvalidOperationException(
            "rethrow was emitted outside a catch handler");
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.RootFrame)));
        ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target,
            checked(slot * layouts.Target.ObjectReferenceSize), layouts.Target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ExceptionTemporary)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ExceptionTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.BeginRethrow)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.RootFrame)));
        ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target,
            checked(slot * layouts.Target.ObjectReferenceSize), layouts.Target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Throw, WasmInstructionOperand.Unsigned((uint)(0))));
        request.Stack.Clear();
    }

    private static void EmitEndFilter(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!request.Context.IsFilterFunclet || request.Stack.Count != 1)
        {
            throw new InvalidOperationException(
                "endfilter was emitted outside a filter funclet");
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.StackBase))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        request.Stack.Clear();
    }

    private void EmitReturn(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var returnSignature = request.Header.MethodInstance?.Signature ??
                              request.Header.Method.Signature;
        if (returnSignature.ReturnSignatureType.StackKind == CliValueKind.ValueType)
        {
            var returnLayout = values.GetValueLayout(
                returnSignature.ReturnSignatureType);
            var source = GetStackLocal(
                request.Context,
                0,
                CliValueKind.ValueType);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(0))));
            valueCopies.Emit(
                code,
                0,
                returnLayout.Size,
                returnLayout.Alignment,
                writer => writer.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)source))),
                request.Context);
        }
        methodFrameExits.Emit(code, request.Context);
        if (returnSignature.ReturnType != CliValueKind.Void &&
            returnSignature.ReturnSignatureType.StackKind != CliValueKind.ValueType)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                0,
                request.Stack[0])))));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        request.Stack.Clear();
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
