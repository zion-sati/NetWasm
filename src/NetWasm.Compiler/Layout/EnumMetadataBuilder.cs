using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

/// <summary>
/// Publishes only the enum metadata needed by compiler intrinsics. Legacy
/// compiler-side consumers use the flat member records. Managed algorithms use
/// a versioned descriptor whose optional value and name arrays have the target
/// machine's native pointer width.
/// </summary>
internal sealed class EnumMetadataBuilder : IEnumMetadataBuilder
{
    private readonly ManagedStaticDataBuildState _state;
    private readonly WasmTargetLayout _target;

    public EnumMetadataBuilder(
        ManagedStaticDataBuildState state,
        WasmTargetLayout target)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Build()
    {
        foreach (var pending in _state.PendingEnumMetadata.Values
                     .OrderBy(metadata => metadata.TypeId))
        {
            var underlyingTypeCode = UnderlyingTypeCode(pending.UnderlyingType);
            if (pending.Payload == EnumMetadataPayload.None)
            {
                _state.EnumMetadata.Add(new EnumMetadataLayout(
                    pending.Type, pending.TypeId, 0,
                    pending.UnderlyingType, pending.IsFlags, []));
                continue;
            }
            var members = pending.Members.Select(member =>
            {
                var nameLayout = new StringLayout(0, 0, 0);
                if ((pending.Payload & EnumMetadataPayload.Names) != 0 &&
                    !_state.Strings.TryGetValue(member.Name, out nameLayout))
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.RuntimeContract,
                        $"enum member name '{member.Name}' was not assigned static storage"));
                }
                return new EnumMetadataMemberLayout(
                    member.Name,
                    member.RawValue,
                    nameLayout);
            })
            // EnumInfo<TStorage> stores signed integer enums in their unsigned
            // storage representation. RawValue already contains the
            // width-normalized bits, so this is the desktop ordering for both
            // signed and unsigned underlying types. LINQ's stable ordering
            // keeps declaration order for aliases with equal values.
            .OrderBy(member => member.RawValue)
            .ToImmutableArray();
            var address = (pending.Payload & EnumMetadataPayload.RuntimeDescriptor) != 0
                ? BuildRuntimeDescriptor(pending, underlyingTypeCode, members)
                : 0;
            _state.EnumMetadata.Add(new EnumMetadataLayout(
                pending.Type,
                pending.TypeId,
                address,
                pending.UnderlyingType,
                pending.IsFlags,
                members));
        }
    }

    private int BuildRuntimeDescriptor(
        PendingEnumMetadata pending,
        int underlyingTypeCode,
        ImmutableArray<EnumMetadataMemberLayout> members)
    {
        var address = ManagedTypeLayoutCompiler.Align(_state.Cursor, sizeof(long));
        var headerSize = checked(16 + (2 * _target.AddressSize));
        var hasValues = (pending.Payload & EnumMetadataPayload.Values) != 0;
        var hasNames = (pending.Payload & EnumMetadataPayload.Names) != 0;
        var valuesAddress = hasValues && members.Length != 0
            ? ManagedTypeLayoutCompiler.Align(address + headerSize, sizeof(ulong))
            : 0;
        var afterValues = valuesAddress == 0
            ? address + headerSize
            : checked(valuesAddress + (members.Length * sizeof(ulong)));
        var namesAddress = hasNames && members.Length != 0
            ? ManagedTypeLayoutCompiler.Align(afterValues, _target.ObjectReferenceAlignment)
            : 0;
        var endAddress = namesAddress == 0
            ? afterValues
            : checked(namesAddress + (members.Length * _target.AddressSize));
        var bytes = new byte[checked(endAddress - address)];

        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), underlyingTypeCode);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(8),
            (pending.IsFlags ? 1 : 0) | (hasValues ? 2 : 0) | (hasNames ? 4 : 0));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), members.Length);
        WriteAddress(bytes.AsSpan(16), valuesAddress);
        WriteAddress(bytes.AsSpan(16 + _target.AddressSize), namesAddress);

        for (var index = 0; index < members.Length; index++)
        {
            if (hasValues)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    bytes.AsSpan(valuesAddress - address + (index * sizeof(ulong))),
                    members[index].RawValue);
            }
            if (hasNames)
            {
                WriteAddress(
                    bytes.AsSpan(namesAddress - address + (index * _target.AddressSize)),
                    members[index].NameLayout.Address);
            }
        }

        _state.Segments.Add(new DataSegment(address, [.. bytes]));
        _state.Cursor = endAddress;
        return address;
    }

    private void WriteAddress(Span<byte> destination, int value)
    {
        if (_target.AddressSize == sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            return;
        }

        BinaryPrimitives.WriteInt64LittleEndian(destination, value);
    }

    private static int UnderlyingTypeCode(CliTypeIdentity type) =>
        type.CanonicalName switch
        {
            "primitive:i1" => 1,
            "primitive:u1" => 2,
            "primitive:i2" => 3,
            "primitive:u2" => 4,
            "primitive:i4" => 5,
            "primitive:u4" => 6,
            "primitive:i8" => 7,
            "primitive:u8" => 8,
            "primitive:char" => 9,
            _ => throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"enum metadata has unsupported underlying type '{type.CanonicalName}'")),
        };
}
