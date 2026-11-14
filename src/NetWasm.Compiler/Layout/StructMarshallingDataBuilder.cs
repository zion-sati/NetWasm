using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

/// <summary>
/// Publishes the structure-marshalling descriptors Marshal reads through
/// StructureMarshallingRuntime, in place of native AOT's struct interop data. Descriptor
/// version 1 (little endian): version, flags, native size, boxed payload offset and field
/// count as 32-bit values, four reserved bytes, then the field offset and field name table
/// addresses at native pointer width (0 when absent). Offsets are 32-bit; names are string
/// references. Only blittable layouts carry a size and fields: they have reference-free
/// sequential or explicit storage whose managed layout is the native one.
/// </summary>
internal sealed class StructMarshallingDataBuilder(
    ReachableProgram program,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IStructMarshallingDataBuilder
{
    private readonly ReachableProgram _program = program ?? throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ?? throw new ArgumentNullException(nameof(state));

    public void Build()
    {
        foreach (var pending in _state.PendingStructMarshalling.OrderBy(pending => pending.TypeId))
            _state.StructMarshalling.Add(new(pending.Type, pending.TypeId, Write(pending)));
    }

    private int Write(PendingStructMarshalling pending)
    {
        var target = _types.Target;
        var address = ManagedTypeLayoutCompiler.Align(_state.Cursor, sizeof(long));
        var headerSize = checked(24 + (2 * target.AddressSize));
        var count = pending.Fields.Length;
        var offsetsAddress = count == 0 ? 0 : address + headerSize;
        var afterOffsets = count == 0 ? address + headerSize : checked(offsetsAddress + (count * sizeof(int)));
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
    ImmutableArray<(string Name, int Offset)> Fields);

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
