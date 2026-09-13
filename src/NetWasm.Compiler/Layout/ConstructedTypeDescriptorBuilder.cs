using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ConstructedTypeDescriptorBuilder(
    ITypeFinder typeFinder,
    ITypeDefinitionResolver typeDefinitions,
    IMetadataIdentityBaseTypeResolver baseTypes,
    IObjectLayoutResolver objectLayouts,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state,
    IStaticReferenceBitmapBuilder bitmaps) : IConstructedTypeDescriptorBuilder
{
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMetadataIdentityBaseTypeResolver _baseTypes = baseTypes ??
        throw new ArgumentNullException(nameof(baseTypes));
    private readonly IObjectLayoutResolver _objectLayouts = objectLayouts ??
        throw new ArgumentNullException(nameof(objectLayouts));
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
        foreach (var (type, layout) in _types.ConstructedObjects
                     .OrderBy(pair => pair.Value.TypeId))
        {
            _state.Cursor = ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment);
            var bitCount = DivideRoundUp(layout.Size, _target.ObjectReferenceSize);
            var bitmap = _bitmaps.Build(
                layout.ReferenceOffsets,
                bitCount,
                _target.ObjectReferenceSize);
            var definition = type.Shape is CliTypeShape.SzArray or CliTypeShape.Array
                ? _typeFinder.FindType("System.Array")
                : _typeDefinitions.ResolveTypeIdentity(type);
            var baseTypeId = type.Shape is CliTypeShape.SzArray or CliTypeShape.Array
                ? _objectLayouts.Resolve(definition.Key).TypeId
                : _baseTypes.GetBaseType(type) is CliTypeIdentity baseType
                    ? _objectLayouts.Resolve(baseType).TypeId
                    : 0;
            _state.ConstructedTypeDescriptors.Add(new ConstructedTypeDescriptorLayout(
                type,
                layout.TypeId,
                baseTypeId,
                layout.Size,
                _state.Cursor,
                bitCount,
                Finalizer: null));
            AddSegment(bitmap);
        }
    }

    private void AddSegment(byte[] data)
    {
        _state.Segments.Add(new DataSegment(_state.Cursor, [.. data]));
        _state.Cursor += data.Length;
    }

    private static int DivideRoundUp(int value, int divisor)
    {
        checked
        {
            return (value + divisor - 1) / divisor;
        }
    }

}
