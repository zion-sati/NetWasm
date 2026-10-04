using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata;

public sealed class ReadonlyTypeFieldValueResolverFactory : IReadonlyTypeFieldValueResolverFactory
{
    public IReadonlyTypeFieldValueResolver Create(
        MetadataCompilationSnapshot metadata,
        IMethodInstanceResolver methods,
        ITypeDefinitionResolver definitions,
        ITypeRepository types,
        EntityKey typeDefinition) => new MetadataReadonlyTypeFieldValueResolver(
            metadata.Assemblies.ToImmutableDictionary(assembly => assembly.Identity),
            methods,
            new MetadataTypeSignatureResolver(
                new MetadataSignatureTypeResolver(new MetadataTypeResolver(definitions), new MetadataTypeIdentityResolver(types)),
                new MetadataStackTypeResolver(definitions)),
            new RawCilInstructionReader(),
            new UnsafeAccessorFieldExposureClassifier(),
            typeDefinition);
}
