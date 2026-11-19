// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

/// <summary>
/// Publishes the structure-marshalling descriptors Marshal reads through
/// StructureMarshallingRuntime, in place of native AOT's struct interop data. Descriptor
/// version 1 (little endian): version, flags, native size, boxed payload offset, field
/// count and conversion step count as 32-bit values, then the field offset and field name
/// table addresses at native pointer width (0 when absent). Offsets are 32-bit; names are
/// string references. Blittable layouts carry a size and fields: they have reference-free
/// sequential or explicit storage whose managed layout is the native one. A struct that
/// holds a bool, char, string, delegate or inline array, and a formatted class, lay out
/// natively as desktop .NET does on Unix: the size and field offsets are native, and the
/// conversion steps (kind, managed offset, native offset, size, count, element, 32-bit each)
/// follow the field offsets. A formatted class's managed offsets count from the object.
/// </summary>
internal sealed class StructMarshallingDataBuilder(
    ReachableProgram program,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IStructMarshallingDataBuilder
{
    private readonly ReachableProgram _program = program ?? throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ?? throw new ArgumentNullException(nameof(state));

    // A conversion step's 32-bit values: kind, managed offset, native offset, size, count, element.
    internal const int StepInts = 6;

    public void Build()
    {
        foreach (var pending in _state.PendingStructMarshalling.OrderBy(pending => pending.TypeId))
        {
            _state.StructMarshalling.Add(new(pending.Type, pending.TypeId, Write(pending))
            {
                Constructor = pending.Constructor,
                PayloadOffset = pending.BoxPayloadOffset,
            });
        }
    }

    private int Write(PendingStructMarshalling pending)
    {
        var target = _types.Target;
        var address = ManagedTypeLayoutCompiler.Align(_state.Cursor, sizeof(long));
        var headerSize = checked(24 + (2 * target.AddressSize));
        var count = pending.Fields.Length;
        var steps = pending.Steps.Length;
        var offsetsAddress = count == 0 ? 0 : address + headerSize;
        var afterOffsets = count == 0
            ? address + headerSize
            : checked(offsetsAddress + (count * sizeof(int)) + (steps * StepInts * sizeof(int)));
        var namesAddress = count == 0 || !_program.StructMarshallingFieldNames
            ? 0
            : ManagedTypeLayoutCompiler.Align(afterOffsets, target.ObjectReferenceAlignment);
        var endAddress = namesAddress == 0 ? afterOffsets : checked(namesAddress + (count * target.AddressSize));
        var bytes = new byte[checked(endAddress - address)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), pending.Flags);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), pending.Size);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), pending.PayloadOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), steps);
        for (var index = 0; index < steps; index++)
        {
            var step = pending.Steps[index];
            var at = bytes.AsSpan(offsetsAddress - address + (count * sizeof(int)) + (index * StepInts * sizeof(int)));
            BinaryPrimitives.WriteInt32LittleEndian(at, (int)step.Kind);
            BinaryPrimitives.WriteInt32LittleEndian(at[4..], step.ManagedOffset);
            BinaryPrimitives.WriteInt32LittleEndian(at[8..], step.NativeOffset);
            BinaryPrimitives.WriteInt32LittleEndian(at[12..], step.Size);
            BinaryPrimitives.WriteInt32LittleEndian(at[16..], step.Count);
            BinaryPrimitives.WriteInt32LittleEndian(at[20..],
                step.Kind is NativeConversionKind.EmbeddedClass or NativeConversionKind.ArrayStructure or
                    NativeConversionKind.ArrayEnum
                    ? step.TypeId
                    : (int)step.Element);
        }
        WriteAddress(bytes.AsSpan(24), offsetsAddress);
        WriteAddress(bytes.AsSpan(24 + target.AddressSize), namesAddress);
        for (var index = 0; index < count; index++)
        {
            var (name, offset) = pending.Fields[index];
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(offsetsAddress - address + (index * sizeof(int))), offset);
            if (namesAddress == 0)
                continue;
            if (!_state.Strings.TryGetValue(name, out var nameLayout))
            {
                throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.RuntimeContract,
                    $"marshalled field name '{name}' was not assigned static storage"));
            }
            WriteAddress(bytes.AsSpan(namesAddress - address + (index * target.AddressSize)), nameLayout.Address);
        }
        _state.Segments.Add(new DataSegment(address, [.. bytes]));
        _state.Cursor = endAddress;
        return address;
    }

    private void WriteAddress(Span<byte> destination, int value)
    {
        if (_types.Target.AddressSize == sizeof(int))
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteInt64LittleEndian(destination, value);
    }
}

internal sealed record PendingStructMarshalling(
    CliTypeIdentity Type,
    int TypeId,
    int Flags,
    int Size,
    int PayloadOffset,
    ImmutableArray<(string Name, int Offset)> Fields)
{
    // A struct that converts natively (StructMarshallingDataCollector.NativeLayout): the steps that copy its managed
    // value to its native layout and back, flattened over nested structs.
    public ImmutableArray<NativeConversionStep> Steps { get; init; } = [];

    // The parameterless constructor PtrToStructure runs on an instance it creates by the runtime type, and where a
    // boxed value's payload, which a value type's constructor receives, starts.
    public MethodInstanceModel? Constructor { get; init; }
    public int BoxPayloadOffset { get; init; }
}

// One step between a structure's managed value and its native layout, offsets from the start of each: a copy of Size
// bytes, a bool as a native boolean of Size bytes, a char as one ANSI byte, a string as a pointer to a native copy or
// inline in Count characters, a delegate as a function pointer, an inline array of Count Element values of
// Size / Count native bytes each, a SafeHandle or CriticalHandle as its handle, an embedded class or an array of
// Count structures through the descriptor of the type whose ID is TypeId, or an array of Count values of the enum
// whose ID is TypeId, copied as its underlying values.
internal readonly record struct NativeConversionStep(NativeConversionKind Kind, int ManagedOffset, int NativeOffset, int Size)
{
    public int Count { get; init; }
    public NativeArrayElement Element { get; init; }
    // An embedded class's or an array's structure or enum type, by its type ID.
    public int TypeId { get; init; }
}

internal enum NativeConversionKind
{
    Copy,
    Boolean,
    Ansi,
    Utf8String,
    Utf16String,
    Utf8Inline,
    Utf16Inline,
    Delegate,
    ArrayCopy,
    ArrayBoolean,
    ArrayAnsi,
    SafeHandle,
    CriticalHandle,
    EmbeddedClass,
    ArrayStructure,
    ArrayEnum,
}

// An inline array's element type, which PtrToStructure allocates the managed array of.
internal enum NativeArrayElement
{
    None,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    IntPtr,
    UIntPtr,
    Boolean,
    Char,
}

internal interface IStructMarshallingDataBuilder
{
    // Writes the descriptors once their field names have static storage.
    void Build();
}

internal sealed class EmptyStructMarshallingDataBuilder : IStructMarshallingDataBuilder
{
    public static EmptyStructMarshallingDataBuilder Instance { get; } = new();

    public void Build()
    {
    }
}
