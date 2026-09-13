using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class StringDataBuilder(
    ITypeFinder typeFinder,
    ReachableProgram program,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state) : IStringDataBuilder
{
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly WasmTargetLayout _target = target ??
        throw new ArgumentNullException(nameof(target));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));

    public void Build()
    {
        var values = _program.StringLiterals
            .Concat(_state.PendingEnumMetadata.Values
                .SelectMany(GetEnumStrings))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (values.Length == 0)
            return;

        var stringObject = GetObjectLayout(_typeFinder.FindType("System.String").Key);
        foreach (var value in values)
        {
            _state.Cursor = ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment);
            var byteLength = ManagedTypeLayoutCompiler.Align(
                _target.ObjectHeaderSize + sizeof(int) + (value.Length * sizeof(char)),
                _target.ObjectReferenceAlignment);
            var bytes = new byte[byteLength];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, stringObject.TypeId);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(_target.ObjectHeaderSize),
                value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(
                        _target.ObjectHeaderSize + sizeof(int) + (index * sizeof(char))),
                    value[index]);
            }
            _state.Strings.Add(value, new StringLayout(
                _state.Cursor,
                value.Length,
                _target.ObjectHeaderSize + sizeof(int)));
            _state.Segments.Add(new DataSegment(_state.Cursor, [.. bytes]));
            _state.Cursor += bytes.Length;
        }
    }

    private static IEnumerable<string> GetEnumStrings(PendingEnumMetadata metadata)
    {
        foreach (var member in metadata.Members)
        {
            yield return member.Name;
            yield return ToDecimal(member.RawValue, metadata.UnderlyingType);
            yield return ToHex(member.RawValue, metadata.UnderlyingType);
        }
        var atoms = metadata.Members
            .Where(member => member.RawValue != 0 &&
                (member.RawValue & (member.RawValue - 1)) == 0)
            .ToArray();
        if (!metadata.IsFlags || atoms.Length > 10)
            yield break;
        for (var mask = 1; mask < (1 << atoms.Length); mask++)
        {
            ulong raw = 0;
            var names = new List<string>();
            for (var index = 0; index < atoms.Length; index++)
            {
                if ((mask & (1 << index)) == 0) continue;
                raw |= atoms[index].RawValue;
                names.Add(atoms[index].Name);
            }
            yield return string.Join(", ", names);
            yield return ToDecimal(raw, metadata.UnderlyingType);
            yield return ToHex(raw, metadata.UnderlyingType);
        }
    }

    private static string ToDecimal(ulong raw, CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i1" => unchecked((sbyte)raw).ToString(CultureInfo.InvariantCulture),
        "primitive:i2" => unchecked((short)raw).ToString(CultureInfo.InvariantCulture),
        "primitive:i4" => unchecked((int)raw).ToString(CultureInfo.InvariantCulture),
        "primitive:i8" => unchecked((long)raw).ToString(CultureInfo.InvariantCulture),
        _ => raw.ToString(CultureInfo.InvariantCulture),
    };

    private static string ToHex(ulong raw, CliTypeIdentity type)
    {
        var name = type.CanonicalName;
        var width = name.EndsWith('1')
            ? 2
            : name.EndsWith('2') ||
                name.EndsWith("char", StringComparison.Ordinal)
                ? 4
                : name.EndsWith('4') ? 8 : 16;
        return raw.ToString($"X{width}", CultureInfo.InvariantCulture);
    }

    private ObjectLayout GetObjectLayout(EntityKey type) =>
        _types.Objects.TryGetValue(type, out var layout)
            ? layout
            : throw Missing(type);

    private static CompilerException Missing(EntityKey type) => new(new CompilerDiagnostic(
        DiagnosticCode.RuntimeContract,
        $"object layout for '{type}' was not generated"));
}
