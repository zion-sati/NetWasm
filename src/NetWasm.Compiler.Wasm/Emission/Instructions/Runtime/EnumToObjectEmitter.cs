// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumToObjectEmitter(
    IEnumMetadataSource metadata,
    ITypeDescriptorSource descriptors,
    ITypeRepository types,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses,
    ITargetLayout layouts,
    IEnumTypeArgumentValidator typeArguments) : IEnumToObjectEmitter
{
    // Enum.ToObject boxes a value as the enum type it is given, at run time. The code is linear in the program's enum
    // and boxed numeric types, not quadratic: the target type id finds the target's group (its underlying type and
    // box layout) by a binary search of type-id runs, the source object's type id finds the source's group the same
    // way, and each group's code is emitted once. One case per enum, each testing every source type, made each call
    // site's code grow with the square of the enum count (megabytes in a large application).
    public void EmitToObject(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var type = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var argumentType = request.Method.Signature.ParameterTypes[1];
        var input = request.Local(1, argumentType);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        var throwFunction = request.Instruction.Context.ManagedThrowFunction;
        typeArguments.Validate(code, type, typeId, throwFunction);
        if (argumentType == CliValueKind.ManagedReference)
        {
            Get(code, input);
            addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.ArgumentNull, throwFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        addresses.Emit(code, 0);
        Set(code, result);
        var targets = TargetGroups();
        if (targets.Groups.Count == 0 && !targets.HasOpen)
            return;
        // The value first, as desktop .NET validates it, then the target. The context's scratch locals hold the
        // groups and the value: a stack slot's i32 local is its reference's on wasm32, and its i64 local on wasm64.
        var group = request.Instruction.Context.NumericTemporaryI4Second;
        var value = request.Instruction.Context.NumericTemporaryI8;
        if (argumentType == CliValueKind.ManagedReference)
        {
            EmitBoxedValue(code, input, group, value, throwFunction);
        }
        else
        {
            Get(code, input);
            if (argumentType != CliValueKind.I8)
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64ExtendI32Signed));
            Set(code, value);
        }
        var targetGroup = group;
        EmitGroupLookup(code, typeId, targets.Runs, targetGroup);
        if (targets.HasOpen)
        {
            // An open generic enum has type metadata but no boxed value representation. Match Enum.ToObject's
            // desktop rejection.
            Get(code, targetGroup);
            WriteI32(code, targets.Groups.Count);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.Argument, throwFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        // A type id that is no enum box this program has leaves the result null.
        for (var index = 0; index < targets.Groups.Count; index++)
        {
            var box = targets.Groups[index];
            Get(code, targetGroup);
            WriteI32(code, index);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            addresses.Emit(code, box.ObjectSize);
            Get(code, typeId);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.Allocate))));
            Set(code, result);
            Get(code, result);
            addresses.Emit(code, box.PayloadOffset);
            addresses.Emit(code, AddressOperation.Add);
            Get(code, value);
            if (box.Underlying.StackKind != CliValueKind.I8)
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
            ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, 0, box.Size);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    // The source object's value as 64 bits: its type id finds its group (a boxed integer, char or bool, or an enum,
    // by underlying type), whose payload is loaded and extended by its signedness. Any other object is rejected.
    private void EmitBoxedValue(
        IWasmInstructionWriter code,
        int input,
        int sourceGroup,
        int value,
        OptionalFunctionIndex throwFunction)
    {
        var sources = SourceGroups();
        Get(code, input);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        Set(code, sourceGroup);
        EmitGroupLookup(code, sourceGroup, sources.Runs, sourceGroup);
        for (var index = 0; index < sources.Groups.Count; index++)
        {
            var group = sources.Groups[index];
            Get(code, sourceGroup);
            WriteI32(code, index);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Get(code, input);
            ManagedMemoryEmitter.EmitLoadByType(code, layouts.Target, group.PayloadOffset, group.Type, group.Size);
            if (group.Type.StackKind != CliValueKind.I8)
            {
                code.Write(WasmInstruction.NoOperand(IsUnsigned(group.Type)
                    ? WasmOpcodes.I64ExtendI32Unsigned
                    : WasmOpcodes.I64ExtendI32Signed));
            }
            Set(code, value);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.Argument, throwFunction);
        for (var index = 0; index < sources.Groups.Count; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    // Sets the group local to the group of the type id in the id local (-1 for none) by a balanced search of runs of
    // consecutive type ids with the same group. The id and group locals may be the same local.
    private static void EmitGroupLookup(IWasmInstructionWriter code, int idLocal, IReadOnlyList<GroupRun> runs, int groupLocal)
    {
        if (runs.Count == 0)
        {
            WriteI32(code, -1);
            Set(code, groupLocal);
            return;
        }
        // The search reads the id; the group, or -1, is written once at the end, as the block's result, so the id and
        // the group may share a local.
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType((byte)WasmValueType.I32)));
        EmitSearch(code, idLocal, runs, 0, runs.Count, 0);
        WriteI32(code, -1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        Set(code, groupLocal);
    }

    // Within the block whose result is the group: a leaf branches out with its group when the id is in its run; a
    // receiver that matches none falls through to the -1 after the search.
    private static void EmitSearch(IWasmInstructionWriter code, int idLocal, IReadOnlyList<GroupRun> runs, int start,
        int count, int depth)
    {
        if (count == 1)
        {
            var run = runs[start];
            WriteI32(code, run.Group);
            Get(code, idLocal);
            if (run.Low == run.High)
            {
                WriteI32(code, run.Low);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            }
            else
            {
                WriteI32(code, run.Low);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
                WriteI32(code, run.High - run.Low + 1);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned((uint)depth)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
            return;
        }
        var leftCount = count / 2;
        var pivot = start + leftCount;
        Get(code, idLocal);
        WriteI32(code, runs[pivot].Low);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitSearch(code, idLocal, runs, start, leftCount, depth + 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitSearch(code, idLocal, runs, pivot, count - leftCount, depth + 1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // The closed enums by box layout (underlying type, box size and payload offset), and open definitions as the
    // group after them.
    private (List<TargetGroup> Groups, List<GroupRun> Runs, bool HasOpen) TargetGroups()
    {
        var groups = new List<TargetGroup>();
        var index = new Dictionary<(string, int, int), int>();
        var ids = new List<(int TypeId, int Group)>();
        var open = new List<int>();
        foreach (var entry in metadata.EnumMetadata)
        {
            if (entry.IsOpenDefinition)
            {
                open.Add(entry.TypeId);
                continue;
            }
            var valueLayout = values.GetValueLayout(entry.UnderlyingType);
            var objectSize = typeLayouts.GetObjectLayout(entry.EnumType).Size;
            var payload = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize, valueLayout.Alignment);
            var key = (entry.UnderlyingType.CanonicalName, objectSize, payload);
            if (!index.TryGetValue(key, out var group))
            {
                group = groups.Count;
                index.Add(key, group);
                groups.Add(new(entry.UnderlyingType, valueLayout.Size, objectSize, payload));
            }
            ids.Add((entry.TypeId, group));
        }
        ids.AddRange(open.Select(typeId => (typeId, groups.Count)));
        return (groups, Runs(ids), open.Count != 0);
    }

    // The boxed numeric types and the enums, by underlying type and payload layout.
    private (List<BoxedNumericSource> Groups, List<GroupRun> Runs) SourceGroups()
    {
        var groups = new List<BoxedNumericSource>();
        var index = new Dictionary<(string, int, int), int>();
        var ids = new List<(int TypeId, int Group)>();
        foreach (var source in GetBoxedNumericSources())
        {
            var key = (source.Type.CanonicalName, source.Size, source.PayloadOffset);
            if (!index.TryGetValue(key, out var group))
            {
                group = groups.Count;
                index.Add(key, group);
                groups.Add(source);
            }
            ids.Add((source.TypeId, group));
        }
        return (groups, Runs(ids));
    }

    // Consecutive type ids of one group become one run, in type-id order.
    private static List<GroupRun> Runs(List<(int TypeId, int Group)> ids)
    {
        var runs = new List<GroupRun>();
        foreach (var (typeId, group) in ids.DistinctBy(id => id.TypeId).OrderBy(id => id.TypeId))
        {
            if (runs.Count != 0 && runs[^1].Group == group && runs[^1].High == typeId - 1)
                runs[^1] = runs[^1] with { High = typeId };
            else
                runs.Add(new(typeId, typeId, group));
        }
        return runs;
    }

    private List<BoxedNumericSource> GetBoxedNumericSources()
    {
        var sources = new List<BoxedNumericSource>();
        foreach (var descriptor in descriptors.TypeDescriptors)
        {
            var type = PrimitiveNumericType(types.GetTypeDefinition(descriptor.Type).FullName);
            if (type is null)
            {
                continue;
            }
            var valueLayout = values.GetValueLayout(type);
            sources.Add(new(
                descriptor.TypeId,
                type,
                valueLayout.Size,
                WasmTargetLayout.Align(
                    layouts.Target.ObjectHeaderSize,
                    valueLayout.Alignment)));
        }
        foreach (var entry in metadata.EnumMetadata)
        {
            if (entry.IsOpenDefinition)
            {
                continue;
            }
            var valueLayout = values.GetValueLayout(entry.UnderlyingType);
            sources.Add(new(
                entry.TypeId,
                entry.UnderlyingType,
                valueLayout.Size,
                WasmTargetLayout.Align(
                    layouts.Target.ObjectHeaderSize,
                    valueLayout.Alignment)));
        }
        return sources;
    }

    private static CliTypeIdentity? PrimitiveNumericType(string fullName) => fullName switch
    {
        "System.Boolean" => CliTypeIdentity.Primitive("bool", CliValueKind.I4),
        "System.Char" => CliTypeIdentity.Primitive("char", CliValueKind.I4),
        "System.SByte" => CliTypeIdentity.Primitive("i1", CliValueKind.I4),
        "System.Byte" => CliTypeIdentity.Primitive("u1", CliValueKind.I4),
        "System.Int16" => CliTypeIdentity.Primitive("i2", CliValueKind.I4),
        "System.UInt16" => CliTypeIdentity.Primitive("u2", CliValueKind.I4),
        "System.Int32" => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        "System.UInt32" => CliTypeIdentity.Primitive("u4", CliValueKind.I4),
        "System.Int64" => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        "System.UInt64" => CliTypeIdentity.Primitive("u8", CliValueKind.I8),
        _ => null,
    };

    private static bool IsUnsigned(CliTypeIdentity type) => type.CanonicalName is
        "primitive:bool" or "primitive:char" or "primitive:u1" or
        "primitive:u2" or "primitive:u4" or "primitive:u8";

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));

    private readonly record struct TargetGroup(CliTypeIdentity Underlying, int Size, int ObjectSize, int PayloadOffset);

    private readonly record struct GroupRun(int Low, int High, int Group);

    private readonly record struct BoxedNumericSource(
        int TypeId,
        CliTypeIdentity Type,
        int Size,
        int PayloadOffset);
}
