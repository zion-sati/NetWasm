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

internal sealed class MemberObjectAllocator(
    ITypeLayoutProvider types,
    IAddressInstructionEmitter addresses,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots) : IMemberObjectAllocator
{
    private readonly ITypeLayoutProvider _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));
    private readonly IRootPublicationEmitter _roots = roots ??
        throw new ArgumentNullException(nameof(roots));

    public void Allocate(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity type,
        int destinationLocal, bool rooted)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(type);
        if (!rooted)
        {
            AllocateObject(request, code, type, destinationLocal);
            return;
        }
        int objectTemporary = request.Instruction.Context.ObjectTemporary;
        AllocateObject(request, code, type, objectTemporary);
        _roots.Emit(request.Instruction, code, constructorCall: true);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)objectTemporary)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)destinationLocal)));
    }

    private void AllocateObject(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int destinationLocal)
    {
        var target = request.Instruction.Target;
        var layout = _types.GetObjectLayout(type);
        if (target.FinalizableTypeIds.Contains(layout.TypeId))
        {
            _addresses.Emit(code, layout.Size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(layout.TypeId)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(
                    RuntimeImportSymbol.AllocateFinalizable, target.RuntimeImportSelection))));
        }
        else
        {
            ObjectAllocationCall.Emit(code, _addresses, _runtimeImports, target, layout);
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)destinationLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)destinationLocal)));
        _addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        _exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
