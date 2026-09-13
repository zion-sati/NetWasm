using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ValueTypeDescriptorBuilder(
    ITypeRepository typeRepository,
    ITypeIdentityResolver identities,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state,
    IStaticReferenceBitmapBuilder bitmaps) : IValueTypeDescriptorBuilder
{
    private readonly ITypeRepository _typeRepository = typeRepository ??
        throw new ArgumentNullException(nameof(typeRepository));
    private readonly ITypeIdentityResolver _identities =
        identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly WasmTargetLayout _target = target ??
        throw new ArgumentNullException(nameof(target));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly IStaticReferenceBitmapBuilder _bitmaps = bitmaps ??
        throw new ArgumentNullException(nameof(bitmaps));

    public void Build()
    {
        foreach (var (key, boxed) in _types.Objects
                     .Where(pair =>
                         _typeRepository.GetTypeDefinition(pair.Key).IsValueType &&
                         _identities.GetTypeIdentity(pair.Key).HasRuntimeStorage)
                     .OrderBy(pair => pair.Value.TypeId))
        {
            AddValueDescriptor(_identities.GetTypeIdentity(key), boxed);
        }
        foreach (var (type, boxed) in _types.ConstructedObjects
                     .Where(pair =>
                         pair.Key.IsValueType && pair.Key.HasRuntimeStorage)
                     .OrderBy(pair => pair.Value.TypeId))
        {
            AddValueDescriptor(type, boxed);
        }
    }

    private void AddValueDescriptor(CliTypeIdentity type, ObjectLayout boxed)
    {
        var value = GetValueLayout(type);
        _state.Cursor = ManagedTypeLayoutCompiler.Align(
            _state.Cursor,
            _target.ObjectReferenceAlignment);
        var bitCount = DivideRoundUp(value.Size, _target.ObjectReferenceSize);
        var bitmap = _bitmaps.Build(
            value.ReferenceOffsets,
            bitCount,
            _target.ObjectReferenceSize);
        _state.ValueTypeDescriptors.Add(new ValueTypeDescriptorLayout(
            type,
            boxed.TypeId,
            value.Size,
            _state.Cursor,
            bitCount));
        _state.Segments.Add(new DataSegment(_state.Cursor, [.. bitmap]));
        _state.Cursor += bitmap.Length;
    }

    private ValueLayout GetValueLayout(CliTypeIdentity type) =>
        _types.ValueLayoutState.Values.TryGetValue(type, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"value layout for '{type}' was not generated"));

    private static int DivideRoundUp(int value, int divisor)
    {
        checked
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
