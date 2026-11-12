using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
        if ((metadata.Payload & EnumMetadataPayload.Names) == 0)
            yield break;

        foreach (var member in metadata.Members)
            yield return member.Name;
    }

    private ObjectLayout GetObjectLayout(EntityKey type) =>
        _types.Objects.TryGetValue(type, out var layout)
            ? layout
            : throw Missing(type);

    private static CompilerException Missing(EntityKey type) => new(new CompilerDiagnostic(
        DiagnosticCode.RuntimeContract,
        $"object layout for '{type}' was not generated"));
}
