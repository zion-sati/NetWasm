using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    IStaticReferenceBitmapBuilder bitmaps,
    IAssignableTypeMetadataBuilder assignableTypes,
    bool requiresTypeFacts = false,
    RuntimeTypeNamePayload typeNamePayload = RuntimeTypeNamePayload.None,
    IAssemblyIdentityFormatter? assemblyNames = null,
    IReadOnlyDictionary<string, MethodInstanceModel>? delegateInvokeDescriptors = null) :
    IConstructedTypeDescriptorBuilder
{
    internal ConstructedTypeDescriptorBuilder(
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        IMetadataIdentityBaseTypeResolver baseTypes,
        IObjectLayoutResolver objectLayouts,
        ManagedTypeLayouts types,
        WasmTargetLayout target,
        ManagedStaticDataBuildState state,
        IStaticReferenceBitmapBuilder bitmaps) :
        this(
            typeFinder,
            typeDefinitions,
            baseTypes,
            objectLayouts,
            types,
            target,
            state,
            bitmaps,
            EmptyAssignableTypeMetadataBuilder.Instance)
    {
    }

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
    private readonly IAssignableTypeMetadataBuilder _assignableTypes = assignableTypes ??
        throw new ArgumentNullException(nameof(assignableTypes));
    private readonly bool _requiresTypeFacts = requiresTypeFacts;
    private readonly RuntimeTypeNamePayload _typeNamePayload = typeNamePayload;
    private readonly IAssemblyIdentityFormatter? _assemblyNames = assemblyNames;
    private readonly IReadOnlyDictionary<string, MethodInstanceModel>
        _delegateInvokeDescriptors = delegateInvokeDescriptors ??
            ImmutableDictionary<string, MethodInstanceModel>.Empty;

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
            var isElementModifier = type.Shape is
                CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer;
            var definition = type.Shape is CliTypeShape.SzArray or CliTypeShape.Array
                ? _typeFinder.FindType("System.Array")
                : isElementModifier
                    ? _typeFinder.FindType("System.Object")
                    : _typeDefinitions.ResolveTypeIdentity(type);
            var baseTypeId = type.Shape is CliTypeShape.SzArray or CliTypeShape.Array
                ? _objectLayouts.Resolve(definition.Key).TypeId
                : isElementModifier
                    ? 0
                : _baseTypes.GetBaseType(type) is CliTypeIdentity baseType
                    ? _objectLayouts.Resolve(baseType).TypeId
                    : 0;
            var bitmapAddress = _state.Cursor;
            AddSegment(bitmap);
            var assignableTypes = _assignableTypes.Build(type);
            var descriptor = new ConstructedTypeDescriptorLayout(
                type,
                layout.TypeId,
                baseTypeId,
                layout.Size,
                bitmapAddress,
                bitCount,
                Finalizer: null)
            {
                AssignableTypeIdsAddress = assignableTypes.Address,
                AssignableTypeIdCount = assignableTypes.Count,
                IsInterface = !isElementModifier && definition.IsInterface,
            };
            _state.ConstructedTypeDescriptors.Add(descriptor);
            if (_requiresTypeFacts)
            {
                _state.PendingTypeFacts.Add(new PendingRuntimeTypeFacts(
                    type,
                    definition,
                    descriptor.TypeId,
                    descriptor.BaseTypeId,
                    descriptor.AssignableTypeIdsAddress,
                    descriptor.AssignableTypeIdCount,
                    _delegateInvokeDescriptors.TryGetValue(
                        type.CanonicalName,
                        out var delegateInvoke)
                            ? delegateInvoke.CanonicalName
                            : null,
                    _typeNamePayload != RuntimeTypeNamePayload.None
                        ? RuntimeTypeNameFormatter.Format(
                            type,
                            _typeDefinitions,
                            _assemblyNames ?? throw new CompilerException(
                                new CompilerDiagnostic(
                                    DiagnosticCode.RuntimeContract,
                                    "runtime type names require an assembly identity formatter")),
                            _typeNamePayload)
                        : null));
            }
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
