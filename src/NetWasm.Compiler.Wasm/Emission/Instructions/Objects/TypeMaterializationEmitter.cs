using System.Numerics;
using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class TypeMaterializationEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IStaticDataLayout staticData,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots,
    ICilTypeOperandResolver types) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.MaterializeType,
            InstructionFamily.AllocationBoxingTypes,
            EmitTypeFromHandle),
        new(
            CilOperation.GetObjectType,
            InstructionFamily.AllocationBoxingTypes,
            EmitObjectType),
        new(
            CilOperation.GetTypeFacts,
            InstructionFamily.AllocationBoxingTypes,
            EmitTypeFacts),
    ];

    private void EmitTypeFromHandle(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CilTypeMaterialization.IsConstant(request.Instruction))
        {
            EmitCommand(request, code, false);
            return;
        }
        EmitStaticTypeObject(request, code);
    }

    // A static Type object's materialization is not a safepoint, so there is no
    // runtime call to fall back to.
    private void EmitStaticTypeObject(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        if (!staticData.TypeObjectAddresses.TryGetValue(type.CanonicalName, out var address))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"typeof({type.CanonicalName}) has no static Type object"));
        }
        var slot = request.Stack.Count - 1;
        addresses.Emit(code, address);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(request.Context, slot, CliValueKind.ManagedReference))));
        request.Stack[slot] = CliValueKind.ManagedReference;
    }

    private void EmitObjectType(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (CilTypeMaterialization.IsExactObjectType(request.Instruction))
        {
            // The receiver can have one type only: check it for null, then use
            // that type's static Type object.
            var slot = request.Stack.Count - 1;
            EmitNullCheck(
                code,
                GetStackLocal(request.Context, slot, CliValueKind.ManagedReference),
                request.Context.ManagedThrowFunction);
            EmitStaticTypeObject(request, code);
            return;
        }
        EmitCommand(
            request,
            code,
            true,
            request.Instruction.Operand is CilOperand.TypeIdentity constrained
                ? constrained.Value
                : null);
    }

    private void EmitTypeFacts(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (staticData.TypeFactsTableAddress <= 0 ||
            staticData.TypeFactsTableCount <= 0)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "type facts were demanded but no lookup table was generated"));
        }

        var slot = request.Stack.Count - 1;
        var source = GetStackLocal(request.Context, slot, CliValueKind.I4);
        var destination = GetStackLocal(
            request.Context,
            slot,
            CliValueKind.NativeInt);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)source)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(staticData.TypeFactsTableCount)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        addresses.Emit(code, staticData.TypeFactsTableAddress);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)source)));
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
        }
        addresses.Emit(code, layouts.Target.AddressSize);
        code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64
            ? WasmOpcodes.I64Multiply
            : WasmOpcodes.I32Multiply));
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(
            layouts.Target.UsesMemory64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                layouts.Target.UsesMemory64 ? 3u : 2u,
                0)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)destination)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)destination)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        request.Stack[slot] = CliValueKind.NativeInt;
    }

    private void EmitCommand(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        bool readObjectHeader,
        CliTypeIdentity? constrainedType = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        roots.Emit(request, code);
        Materialize(
            code,
            request.Stack,
            request.Context,
            readObjectHeader,
            constrainedType);
    }

    private void Materialize(
        IWasmInstructionWriter code,
        List<CliValueKind> stack,
        MethodEmissionContext context,
        bool readObjectHeader,
        CliTypeIdentity? constrainedType)
    {
        var slot = stack.Count - 1;
        var typeIdLocal = GetStackLocal(context, slot, CliValueKind.I4);
        var typeLocal = GetStackLocal(context, slot, CliValueKind.ManagedReference);
        if (constrainedType is { IsValueType: true })
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(
                    typeLayouts.GetObjectLayout(constrainedType).TypeId)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        }
        else if (readObjectHeader)
        {
            if (constrainedType is not null)
            {
                var addressLocal = GetStackLocal(
                    context,
                    slot,
                    CliValueKind.ManagedAddress);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)addressLocal)));
                ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, 0);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)typeLocal)));
            }
            EmitNullCheck(code, typeLocal, context.ManagedThrowFunction);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(typeLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(typeIdLocal))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(typeIdLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        // The type id and the Type object can share the slot's local, so the
        // cached object stays in the object temporary until the id is no longer needed.
        var cached = readObjectHeader && staticData.TypeObjectCacheAddress != 0;
        var cachedLocal = context.ObjectTemporary;
        if (cached)
        {
            EmitCachedTypeObject(code, typeIdLocal, cachedLocal);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)cachedLocal)));
            EmitReferenceEqualZero(code);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(typeIdLocal))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.TypeTypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.GetTypeObject)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(typeLocal))));
        EmitAllocationFailureCheck(code, typeLocal, context.ManagedThrowFunction);
        if (cached)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)cachedLocal)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)typeLocal)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        stack[slot] = CliValueKind.ManagedReference;
    }

    // A type's Type object, once the runtime has made it, is in the runtime's
    // cache at the type id, as desktop .NET reads it from the method table;
    // cachedLocal is left zero when the cache or its entry is empty. The runtime
    // initializer stores the cache's address, which a host without one leaves zero.
    private void EmitCachedTypeObject(IWasmInstructionWriter code, int typeIdLocal, int cachedLocal)
    {
        var target = layouts.Target;
        addresses.Emit(code, staticData.TypeObjectCacheAddress);
        ManagedMemoryEmitter.EmitLoadBySize(code, target, 0, target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)cachedLocal)));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)cachedLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        var shift = BitOperations.Log2((uint)target.ObjectReferenceSize);
        if (target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Unsigned));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(shift)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ShiftLeft));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(shift)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32ShiftLeft));
        }
        addresses.Emit(code, AddressOperation.Add);
        ManagedMemoryEmitter.EmitLoadBySize(code, target, 0, target.ObjectReferenceSize);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)cachedLocal)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitNullCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitAllocationFailureCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitReferenceEqualZero(IWasmInstructionWriter code)
    {
        addresses.Emit(code, AddressOperation.EqualZero);
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
