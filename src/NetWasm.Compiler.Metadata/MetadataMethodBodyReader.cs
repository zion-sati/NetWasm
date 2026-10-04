using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodBodyReader(
    ICilDecoderFactory decoders,
    IManagedAssemblyResolver assemblies,
    IMetadataMethodReferenceResolver methodReferences,
    ISymbolFormatter symbols,
    IUnsafeAccessorTargetResolverFactory accessors,
    IUnsafeAccessorBodyBuilder accessorBodies) : IMethodBodyReader
{
    private readonly Lazy<IUnsafeAccessorTargetResolver> _accessors = new(accessors.Create);

    public CilMethodBody ReadMethodBody(MethodDefinitionModel method)
    {
        var assembly = assemblies.Resolve(method.Key.Assembly);
        return !method.HasBody && method.UnsafeAccessor is not null
            ? ReadMethodBody(methodReferences.Resolve(assembly.Metadata, method.Key.MetadataToken, symbols.Format(method), 0))
            : decoders.Create().Decode(assembly, method);
    }

    public CilMethodBody ReadMethodBody(MethodInstanceModel method) =>
        !method.Definition.HasBody && method.Definition.UnsafeAccessor is not null
            ? accessorBodies.Build(method, _accessors.Value.Resolve(method))
            : decoders.Create().Decode(assemblies.Resolve(method.Definition.Key.Assembly), method);
}
