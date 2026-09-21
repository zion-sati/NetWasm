using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class TypeDescriptorBuilder(
    ITypeIdentityResolver identities,
    IMetadataIdentityBaseTypeResolver baseTypes,
    IObjectLayoutResolver objectLayouts,
    ReachableProgram program,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state,
    IStaticReferenceBitmapBuilder bitmaps,
    IAssignableTypeMetadataBuilder assignableTypes) : ITypeDescriptorBuilder
{
    internal TypeDescriptorBuilder(
        ITypeIdentityResolver identities,
        IMetadataIdentityBaseTypeResolver baseTypes,
        IObjectLayoutResolver objectLayouts,
        ReachableProgram program,
        ManagedTypeLayouts types,
        WasmTargetLayout target,
        ManagedStaticDataBuildState state,
        IStaticReferenceBitmapBuilder bitmaps) :
        this(
            identities,
            baseTypes,
            objectLayouts,
            program,
            types,
            target,
            state,
            bitmaps,
            EmptyAssignableTypeMetadataBuilder.Instance)
    {
    }

    private readonly ITypeIdentityResolver _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly IMetadataIdentityBaseTypeResolver _baseTypes = baseTypes ??
        throw new ArgumentNullException(nameof(baseTypes));
    private readonly IObjectLayoutResolver _objectLayouts = objectLayouts ??
        throw new ArgumentNullException(nameof(objectLayouts));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly WasmTargetLayout _target = target ??
        throw new ArgumentNullException(nameof(target));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly IStaticReferenceBitmapBuilder _bitmaps = bitmaps ??
        throw new ArgumentNullException(nameof(bitmaps));
    private readonly IAssignableTypeMetadataBuilder _assignableTypes = assignableTypes ??
        throw new ArgumentNullException(nameof(assignableTypes));

    public void Build()
    {
        foreach (var (type, layout) in _types.Objects.OrderBy(pair => pair.Value.TypeId))
        {
            _state.Cursor = ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment);
            var bitCount = DivideRoundUp(layout.Size, _target.ObjectReferenceSize);
            var bitmap = _bitmaps.Build(
                layout.ReferenceOffsets,
                bitCount,
                _target.ObjectReferenceSize);
            var bitmapAddress = _state.Cursor;
            AddSegment(bitmap);
            var typeIdentity = _identities.GetTypeIdentity(type);
            var assignableTypes = _assignableTypes.Build(typeIdentity);
            _state.TypeDescriptors.Add(new TypeDescriptorLayout(
                type,
                layout.TypeId,
                _baseTypes.GetBaseType(typeIdentity) is
                    CliTypeIdentity baseType
                    ? _objectLayouts.Resolve(baseType).TypeId
                    : 0,
                layout.Size,
                bitmapAddress,
                bitCount,
                _program.Finalizers.TryGetValue(type, out var finalizer)
                    ? finalizer
                    : null)
            {
                AssignableTypeIdsAddress = assignableTypes.Address,
                AssignableTypeIdCount = assignableTypes.Count,
            });
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
