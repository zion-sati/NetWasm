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
    ManagedStaticDataBuildState state,
    IAssignableTypeMetadataBuilder assignableTypes,
    IReadOnlyDictionary<CliTypeIdentity, MethodInstanceModel>? finalizers = null) :
    IConstructedTypeDescriptorBuilder
{
    internal ConstructedTypeDescriptorBuilder(
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        IMetadataIdentityBaseTypeResolver baseTypes,
        IObjectLayoutResolver objectLayouts,
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state) :
        this(
            typeFinder,
            typeDefinitions,
            baseTypes,
            objectLayouts,
            types,
            state,
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
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly IAssignableTypeMetadataBuilder _assignableTypes = assignableTypes ??
        throw new ArgumentNullException(nameof(assignableTypes));
    private readonly IReadOnlyDictionary<CliTypeIdentity, MethodInstanceModel> _finalizers =
        finalizers ?? ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty;

    public void Build()
    {
        foreach (var (type, layout) in _types.ConstructedObjects
                     .OrderBy(pair => pair.Value.TypeId))
        {
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
            var descriptor = new ConstructedTypeDescriptorLayout(
                type,
                layout.TypeId,
                baseTypeId,
                layout.Size,
                layout.ReferenceOffsets,
                _finalizers.TryGetValue(type, out var finalizer) ? finalizer : null)
            {
                AssignableTypeIds = _assignableTypes.Build(type),
                IsInterface = !isElementModifier && definition.IsInterface,
            };
            _state.ConstructedTypeDescriptors.Add(descriptor);
            _state.PendingTypeFacts.Add(new PendingRuntimeTypeFacts(type, definition, descriptor.TypeId));
        }
    }
}
