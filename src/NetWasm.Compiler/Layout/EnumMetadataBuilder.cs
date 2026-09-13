using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

/// <summary>
/// Publishes only the enum metadata needed by compiler intrinsics. The record
/// format is intentionally flat: a 16-byte header followed by 16-byte member
/// records containing a static string object address/length and the underlying
/// bits split into two little-endian words.
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
            _state.Cursor = ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment);
            var address = _state.Cursor;
            var members = pending.Members.Select(member =>
            {
                if (!_state.Strings.TryGetValue(member.Name, out var nameLayout))
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
            var bytes = new byte[checked(16 + (members.Length * 16))];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, pending.TypeId);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(4), UnderlyingTypeCode(pending.UnderlyingType));
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(8), pending.IsFlags ? 1 : 0);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(12), members.Length);
            for (var index = 0; index < members.Length; index++)
            {
                var member = members[index];
                var offset = 16 + (index * 16);
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(offset), member.NameLayout.Address);
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(offset + 4), member.NameLayout.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(offset + 8), unchecked((uint)member.RawValue));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(offset + 12), unchecked((uint)(member.RawValue >> 32)));
            }
            _state.Segments.Add(new DataSegment(address, [.. bytes]));
            _state.EnumMetadata.Add(new EnumMetadataLayout(
                pending.Type,
                pending.TypeId,
                address,
                pending.UnderlyingType,
                pending.IsFlags,
                members));
            _state.Cursor += bytes.Length;
        }
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
