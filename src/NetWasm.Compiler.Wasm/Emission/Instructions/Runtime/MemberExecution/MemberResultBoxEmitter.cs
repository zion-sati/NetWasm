using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

internal sealed class MemberResultBoxEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ITypeLayoutProvider types,
    IAddressInstructionEmitter addresses,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions) : IMemberResultBoxEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly ITypeLayoutProvider _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity resultType)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(resultType);
        var valueLocal = PreserveResult(request, code, resultType.StackKind);
        int resultLocal = request.Local(0, CliValueKind.ManagedReference);
        if (resultType.StackKind == CliValueKind.ManagedReference)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueLocal)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)resultLocal)));
            return;
        }

        var valueLayout = _values.GetValueLayout(resultType);
        var objectLayout = _types.GetObjectLayout(resultType);
        int payloadOffset = WasmTargetLayout.Align(
            _layouts.Target.ObjectHeaderSize,
            valueLayout.Alignment);
        _addresses.Emit(code, objectLayout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(objectLayout.TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)_runtimeImports.Resolve(RuntimeImportSymbol.Allocate))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)resultLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)resultLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        _exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)resultLocal)));
        _addresses.Emit(code, payloadOffset);
        _addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)valueLocal)));
        if (resultType.StackKind == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F32ReinterpretI32));
        }
        else if (resultType.StackKind == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.F64ReinterpretI64));
        }
        ManagedMemoryEmitter.EmitStoreByType(
            code,
            _layouts.Target,
            0,
            resultType,
            valueLayout.Size, scratch: request.Instruction.Context.VectorScratch);
    }

    // Keeps the value in a temporary while the box is allocated; floating values keep their bits.
    private static int PreserveResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliValueKind kind)
    {
        int local;
        if (kind == CliValueKind.ManagedReference)
        {
            local = request.Local(0, kind);
        }
        else if (kind is CliValueKind.I4 or CliValueKind.F4)
        {
            local = request.Instruction.Context.NumericTemporaryI4;
        }
        else
        {
            local = request.Instruction.Context.NumericTemporaryI8;
        }
        if (kind == CliValueKind.F4)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ReinterpretF32));
        }
        else if (kind == CliValueKind.F8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ReinterpretF64));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
        return local;
    }
}
