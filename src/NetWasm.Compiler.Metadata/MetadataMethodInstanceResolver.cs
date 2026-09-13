using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodInstanceResolver(
    IManagedAssemblyResolver assemblies,
    IMetadataMethodReferenceResolver references) : IMethodInstanceResolver
{
    public MethodInstanceModel ResolveMethodInstance(
        AssemblyIdentity source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset,
        CliGenericContext? genericContext = null) =>
        references.Resolve(
            assemblies.Resolve(source).Metadata,
            metadataToken,
            methodDisplayName,
            ilOffset,
            genericContext);
}
