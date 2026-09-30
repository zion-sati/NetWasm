using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataPropertyAccessorResolver : IMetadataPropertyAccessorResolver
{
    private readonly ImmutableDictionary<EntityKey, PropertyDefinitionModel>
        _propertiesByAccessor;
    private readonly IMethodInstanceResolver _methods;

    public MetadataPropertyAccessorResolver(
        IReadOnlyCollection<PropertyDefinitionModel> properties,
        IMethodInstanceResolver methods)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        var propertiesByAccessor = ImmutableDictionary.CreateBuilder<
            EntityKey,
            PropertyDefinitionModel>();
        foreach (var property in properties)
        {
            Add(property, property.Getter);
            Add(property, property.Setter);
        }
        _propertiesByAccessor = propertiesByAccessor.ToImmutable();

        void Add(PropertyDefinitionModel property, EntityKey? accessor)
        {
            if (accessor is not EntityKey key)
            {
                return;
            }
            if (!propertiesByAccessor.TryAdd(key, property))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    $"method '{key}' is associated with multiple properties"));
            }
        }
    }

    public PropertyInstanceModel? Resolve(MethodInstanceModel accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (!_propertiesByAccessor.TryGetValue(
                accessor.Definition.Key,
                out var property))
        {
            return null;
        }

        var typeArguments = accessor.DeclaringType.Shape ==
                CliTypeShape.GenericInstantiation
            ? accessor.DeclaringType.TypeArguments
            : [];
        var context = new CliGenericContext(typeArguments, []);
        return new PropertyInstanceModel(
            property,
            accessor.DeclaringType,
            property.PropertyType.Substitute(typeArguments),
            [.. property.IndexParameterTypes.Select(type =>
                type.Substitute(typeArguments))],
            ResolveAccessor(property.Getter),
            ResolveAccessor(property.Setter));

        MethodInstanceModel? ResolveAccessor(EntityKey? key)
        {
            if (key is not EntityKey value)
            {
                return null;
            }
            return value == accessor.Definition.Key
                ? accessor
                : _methods.ResolveMethodInstance(
                    value.Assembly,
                    value.MetadataToken,
                    property.Name,
                    ilOffset: 0,
                    context);
        }
    }
}
