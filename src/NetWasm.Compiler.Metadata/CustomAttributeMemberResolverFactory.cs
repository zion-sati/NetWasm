using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class CustomAttributeMemberResolverFactory(
    IMetadataCompilationMaterializationFactory materializations) : ICustomAttributeMemberResolverFactory
{
    public ICustomAttributeMemberResolver Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities,
        ITypeRepository types, IFieldRepository fields, IMethodRepository methods,
        IMethodInstanceResolver methodInstances)
    {
        var sources = metadata.Assemblies.ToImmutableDictionary(assembly => assembly.Identity,
            assembly => assembly.Metadata);
        var signatures = new MetadataSignatureTypeResolver(new MetadataTypeResolver(definitions), identities);
        var fieldInstances = new MetadataFieldReferenceResolver(new MetadataEntityHandleReader(), signatures,
            new FieldSignatureContextResolver(), types, definitions, fields, new MetadataStackTypeResolver(definitions),
            new FrameworkFieldContractValidator(new MetadataAssemblyResolver(
                sources.ToImmutableDictionary(pair => pair.Key.Name, pair => pair.Value, StringComparer.Ordinal),
                ImmutableDictionary<string, string>.Empty,
                new MetadataAvailabilityValidator(new MetadataLifetime()))),
            new RuntimeTypeDefinitions(sources.ToImmutableDictionary(pair => pair.Key.Name, pair => pair.Value, StringComparer.Ordinal)),
            materializations.Create(metadata).OriginalContractTypes,
            new MetadataCrossAssemblyPrivateAccessValidator(new MetadataAssemblyResolver(
                sources.ToImmutableDictionary(pair => pair.Key.Name, pair => pair.Value, StringComparer.Ordinal),
                ImmutableDictionary<string, string>.Empty,
                new MetadataAvailabilityValidator(new MetadataLifetime()))));
        return new CustomAttributeMemberResolver(sources,
            [.. metadata.Assemblies.SelectMany(assembly => assembly.Properties.Values)], definitions,
            fields, methods, fieldInstances, methodInstances);
    }
}
