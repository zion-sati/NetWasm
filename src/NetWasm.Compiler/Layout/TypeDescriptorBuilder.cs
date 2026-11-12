using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class TypeDescriptorBuilder(
    ITypeIdentityResolver identities,
    ITypeDefinitionResolver typeDefinitions,
    IMetadataIdentityBaseTypeResolver baseTypes,
    IObjectLayoutResolver objectLayouts,
    ReachableProgram program,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state,
    IAssignableTypeMetadataBuilder assignableTypes) : ITypeDescriptorBuilder
{
    internal TypeDescriptorBuilder(
        ITypeIdentityResolver identities,
        ITypeDefinitionResolver typeDefinitions,
        IMetadataIdentityBaseTypeResolver baseTypes,
        IObjectLayoutResolver objectLayouts,
        ReachableProgram program,
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state) :
        this(
            identities,
            typeDefinitions,
            baseTypes,
            objectLayouts,
            program,
            types,
            state,
            EmptyAssignableTypeMetadataBuilder.Instance)
    {
    }

    private readonly ITypeIdentityResolver _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMetadataIdentityBaseTypeResolver _baseTypes = baseTypes ??
        throw new ArgumentNullException(nameof(baseTypes));
    private readonly IObjectLayoutResolver _objectLayouts = objectLayouts ??
        throw new ArgumentNullException(nameof(objectLayouts));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly IAssignableTypeMetadataBuilder _assignableTypes = assignableTypes ??
        throw new ArgumentNullException(nameof(assignableTypes));

    public void Build()
    {
        foreach (var (type, layout) in _types.Objects.OrderBy(pair => pair.Value.TypeId))
        {
            var typeIdentity = _identities.GetTypeIdentity(type);
            var descriptor = new TypeDescriptorLayout(
                type,
                layout.TypeId,
                GetBaseTypeId(typeIdentity),
                layout.Size,
                layout.ReferenceOffsets,
                _program.Finalizers.TryGetValue(typeIdentity, out var finalizer)
                    ? finalizer
                    : null)
            {
                AssignableTypeIds = _assignableTypes.Build(typeIdentity),
                IsInterface = _typeDefinitions.ResolveTypeIdentity(typeIdentity).IsInterface,
            };
            _state.TypeDescriptors.Add(descriptor);
            _state.PendingTypeFacts.Add(new PendingRuntimeTypeFacts(
                typeIdentity,
                _typeDefinitions.ResolveTypeIdentity(typeIdentity),
                descriptor.TypeId));
        }
    }

    private int GetBaseTypeId(CliTypeIdentity type)
    {
        var baseType = _baseTypes.GetBaseType(type);
        if (baseType is null || baseType.ContainsGenericParameters)
        {
            // Open generic definitions are retained for runtime metadata only. A base such as
            // Base<T> has no physical object layout until T is closed; closed descendants use
            // ConstructedTypeDescriptorBuilder and retain their exact constructed base instead.
            return 0;
        }
        return _objectLayouts.Resolve(baseType).TypeId;
    }
}
