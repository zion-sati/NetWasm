using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed class UnsafeAccessorTargetResolverFactory(
    MetadataCompilationMaterialization metadata,
    ITypeDefinitionResolver definitions,
    ITypeRepository types,
    IMethodRepository methods,
    IFieldRepository fields,
    IMetadataStackTypeResolver stackTypes) : IUnsafeAccessorTargetResolverFactory
{
    public IUnsafeAccessorTargetResolver Create()
    {
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        var finder = new MetadataTypeFinder([.. metadata.Types.Values], availability);
        var objectType = CliTypeIdentity.FromDefinition(finder.FindType("System.Object"));
        var valueType = CliTypeIdentity.FromDefinition(finder.FindType("System.ValueType"));
        var signatures = new UnsafeAccessorSignatureReader(metadata.MetadataAssemblies);
        var comparer = new UnsafeAccessorSignatureComparer(definitions);
        var assemblies = new MetadataAssemblyResolver(metadata.MetadataAssemblies,
            metadata.ReferenceAssemblyAliases, availability);
        var signatureTypes = new MetadataSignatureTypeResolver(
            new MetadataTypeResolver(definitions), new MetadataTypeIdentityResolver(types));
        var assignability = new UnsafeAccessorTypeAssignability(comparer, definitions,
            new MetadataBaseTypeIdentityResolver(definitions, assemblies, signatureTypes),
            new MetadataImplementedInterfaceResolver(definitions, assemblies, signatureTypes), finder, objectType, valueType);
        var constraints = new MetadataUnsafeAccessorConstraintReader(
            metadata.MetadataAssemblies.Values.ToImmutableDictionary(assembly => assembly.Identity), definitions);
        var validator = new UnsafeAccessorConstraintValidator(comparer, assignability, definitions, methods, objectType, valueType);
        var methodResolver = new UnsafeAccessorMethodResolver(methods, signatures, comparer, constraints, validator);
        var fieldResolver = new UnsafeAccessorFieldResolver(fields, signatures, comparer);
        var members = ImmutableDictionary<UnsafeAccessorMemberKind, IUnsafeAccessorMemberResolver>.Empty
            .Add(UnsafeAccessorMemberKind.Constructor, methodResolver)
            .Add(UnsafeAccessorMemberKind.Method, methodResolver)
            .Add(UnsafeAccessorMemberKind.StaticMethod, methodResolver)
            .Add(UnsafeAccessorMemberKind.Field, fieldResolver)
            .Add(UnsafeAccessorMemberKind.StaticField, fieldResolver);
        return new UnsafeAccessorTargetResolver(signatures, definitions, members, finder, methods, stackTypes);
    }
}
