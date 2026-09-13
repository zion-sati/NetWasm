using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ValueTypeLayoutResolver(
    ITypeRepository typeRepository,
    IFieldRepository fields,
    ITypeIdentityResolver identities,
    IValueLayoutResolver valueLayouts,
    ManagedTypeLayoutBuildState state,
    ImmutableArray<TypeDefinitionModel> typeInventory) : IValueTypeLayoutResolver
{
    private readonly ITypeRepository _typeRepository = typeRepository ??
        throw new ArgumentNullException(nameof(typeRepository));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ITypeIdentityResolver _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly IValueLayoutResolver _valueLayouts = valueLayouts ??
        throw new ArgumentNullException(nameof(valueLayouts));
    private readonly ManagedTypeLayoutBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly ImmutableArray<TypeDefinitionModel> _typeInventory = typeInventory;

    public void Resolve()
    {
        foreach (var key in _state.Objects.Keys.Where(key =>
                     _typeRepository.GetTypeDefinition(key).IsValueType
            && _identities.GetTypeIdentity(key).HasRuntimeStorage))
        {
            _valueLayouts.Resolve(_identities.GetTypeIdentity(key));
        }
        foreach (var type in _typeInventory.Where(type =>
                     type.IsValueType &&
                     type.GenericArity == 0 &&
                     type.LayoutKind != CliTypeLayoutKind.Explicit &&
                     type.PackingSize == 0 &&
                     (type.DeclaredSize == 0 ||
                      type.DeclaredSize == 1 &&
                      !type.Fields.Select(_fields.GetField)
                          .Any(field => !field.IsStatic))
                     && _identities.GetTypeIdentity(type.Key).HasRuntimeStorage))
        {
            _valueLayouts.Resolve(_identities.GetTypeIdentity(type.Key), type);
        }
    }
}
