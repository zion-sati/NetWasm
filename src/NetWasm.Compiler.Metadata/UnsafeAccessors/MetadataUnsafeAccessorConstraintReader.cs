using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

// Adapter: preserve the typical declaration's type/method parameter indices.
internal sealed class MetadataUnsafeAccessorConstraintReader(
    ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> assemblies,
    ITypeDefinitionResolver definitions) : IUnsafeAccessorConstraintReader
{
    public UnsafeAccessorGenericConstraints Read(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var assembly = assemblies[method.Key.Assembly];
        var reader = assembly.Reader;
        var provider = new SignatureTypeProvider(assembly.Identity, reader, assembly.AssemblyIdentityAliases);
        var type = reader.GetTypeDefinition((TypeDefinitionHandle)MetadataTokens.EntityHandle(method.DeclaringType.MetadataToken));
        var definition = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(method.Key.MetadataToken));
        return new(ReadParameters(type.GetGenericParameters()), ReadParameters(definition.GetGenericParameters()));

        ImmutableArray<UnsafeAccessorGenericParameterConstraints> ReadParameters(GenericParameterHandleCollection handles)
        {
            var parameters = ImmutableArray.CreateBuilder<UnsafeAccessorGenericParameterConstraints>();
            foreach (var handle in handles)
            {
                var parameter = reader.GetGenericParameter(handle);
                if (parameter.Index != parameters.Count)
                    throw new BadImageFormatException("Generic parameter indices must be contiguous and ordered.");
                var types = ImmutableArray.CreateBuilder<CliTypeIdentity>();
                foreach (var constraintHandle in parameter.GetConstraints())
                {
                    var constraint = reader.GetGenericParameterConstraint(constraintHandle).Type;
                    types.Add(constraint.Kind switch
                    {
                        HandleKind.TypeDefinition => Canonical(provider.GetTypeFromDefinition(reader, (TypeDefinitionHandle)constraint, 0)),
                        HandleKind.TypeReference => Canonical(provider.GetTypeFromReference(reader, (TypeReferenceHandle)constraint, 0)),
                        // SRM's TypeDefOrRefOrSpec coded handle guarantees these
                        // three forms. TypeSpec preserves its encoded kind/index.
                        _ => provider.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)constraint, 0),
                    });
                }
                parameters.Add(new(parameter.Attributes, types.ToImmutable()));
            }
            return parameters.ToImmutable();
        }

        CliTypeIdentity Canonical(CliTypeIdentity identity) => CliTypeIdentity.FromDefinition(definitions.ResolveTypeIdentity(identity));
    }
}
