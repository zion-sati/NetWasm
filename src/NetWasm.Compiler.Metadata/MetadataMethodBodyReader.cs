using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodBodyReader(
    ICilDecoderFactory decoders,
    IManagedAssemblyResolver assemblies,
    IMetadataMethodReferenceResolver methodReferences,
    ISymbolFormatter symbols,
    IUnsafeAccessorTargetResolverFactory accessors,
    IUnsafeAccessorBodyBuilder accessorBodies,
    IDelegateAsynchronousCallBodyBuilder delegateAsynchronousCalls,
    IUnsafeIntrinsicBodyBuilder unsafeIntrinsics,
    IUnboundNativeImportBodyBuilder unboundNativeImports) : IMethodBodyReader
{
    private readonly Lazy<IUnsafeAccessorTargetResolver> _accessors = new(accessors.Create);

    // A body decodes for its method's instance, which the runtime-provided
    // bodies take too.
    public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => ReadMethodBody(methodReferences.Resolve(
        assemblies.Resolve(method.Key.Assembly).Metadata, method.Key.MetadataToken, symbols.Format(method), 0));

    public CilMethodBody ReadMethodBody(MethodInstanceModel method) => method.Definition switch
    {
        { HasBody: false, UnsafeAccessor: not null } => accessorBodies.Build(method, _accessors.Value.Resolve(method)),
        { HasBody: false, IsDelegateAsynchronousCall: true } => delegateAsynchronousCalls.Build(method),
        { HasBody: false, UnboundNativeImport: not null } => unboundNativeImports.Build(method),
        _ when unsafeIntrinsics.Build(method) is { } intrinsic => intrinsic,
        _ => decoders.Create().Decode(assemblies.Resolve(method.Definition.Key.Assembly), method),
    };
}
