// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeImports;

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
    IUnboundNativeImportBodyBuilder unboundNativeImports,
    INativeMarshallingStubBodyBuilder nativeMarshallingStubs,
    IMissingMemberBodyBuilder missingMembers,
    IMissingTypeFinder missingTypes,
    IRuntimeAsyncBodyBuilder runtimeAsync,
    IRuntimeAsyncResumerBodyBuilder runtimeAsyncResumers,
    IRuntimeAsyncEntryBuilder runtimeAsyncEntries,
    IRuntimeAsyncSourceReader runtimeAsyncSources,
    IMethodRepository methods,
    IRuntimeAsyncBodyPlacement runtimeAsyncBodies,
    INativeImportForwarderBodyBuilder nativeImportForwarders,
    INativeImportAddressRedirector nativeImportAddresses) : IMethodBodyReader
{
    private readonly Lazy<IUnsafeAccessorTargetResolver> _accessors = new(accessors.Create);

    // A body decodes for its method's instance, which the runtime-provided
    // bodies take too.
    public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => ReadMethodBody(method.RuntimeAsyncBodyOf is { } of
        ? RuntimeAsyncBodies.Over(method, Resolve(methods.GetMethod(of)))
        : Resolve(method));

    // A definition's instance over its own generic parameters; a runtime-async method's separate body has its method's,
    // which metadata records (RuntimeAsyncBodies).
    private MethodInstanceModel Resolve(MethodDefinitionModel method) => methodReferences.Resolve(
        assemblies.Resolve(method.Key.Assembly).Metadata, method.Key.MetadataToken, symbols.Format(method), 0);

    public CilMethodBody ReadMethodBody(MethodInstanceModel method) => method.Definition switch
    {
        { IsRuntimeAsync: true } when runtimeAsyncBodies.Separates(method.Definition) => runtimeAsyncEntries.Build(method),
        { IsRuntimeAsync: true } => runtimeAsync.Build(method, runtimeAsyncSources.Read(method)),
        { HasBody: false, RuntimeAsyncBodyOf: not null } =>
            runtimeAsync.Build(method, runtimeAsyncSources.Read(RuntimeAsyncBodies.Of(methods, method))),
        { HasBody: false, UnsafeAccessor: not null } => accessorBodies.Build(method, _accessors.Value.Resolve(method)),
        { HasBody: false, IsDelegateAsynchronousCall: true } => delegateAsynchronousCalls.Build(method),
        { HasBody: false, UnboundNativeImport: not null } => unboundNativeImports.Build(method),
        { HasBody: false, NativeMarshallingStub: not null } => nativeMarshallingStubs.Build(method),
        { HasBody: false, ForwardsNativeImport: not null } => nativeImportForwarders.Build(method),
        { HasBody: false, RuntimeAsyncResumer: not null } => runtimeAsyncResumers.Build(method),
        _ when unsafeIntrinsics.Build(method) is { } intrinsic => intrinsic,
        _ => Decode(method),
    };

    // A method whose IL names a member its declaring type lacks, or a type its assembly lacks, compiles to a body
    // that throws when called, as .NET fails such a method when it first compiles it; the compilation reports it
    // (NW1016). A type a member reference resolves surfaces while decoding, and a type token after it.
    private CilMethodBody Decode(MethodInstanceModel method)
    {
        CilMethodBody body;
        try
        {
            body = decoders.Create().Decode(assemblies.Resolve(method.Definition.Key.Assembly), method);
        }
        catch (CompilerException error) when (error.Diagnostic is { Code: DiagnosticCode.MissingApi, MissingMember: { } missing })
        {
            return missingMembers.Build(method, missing);
        }
        catch (CompilerException error) when (error.Diagnostic.MissingMember is { Kind: MissingMemberKind.Type } type)
        {
            return missingMembers.Build(method, type with
            {
                Method = error.Diagnostic.Method ?? symbols.Format(method.Definition),
                IlOffset = error.Diagnostic.IlOffset ?? 0,
            });
        }
        return missingTypes.Find(body, symbols.Format(method.Definition)) is { } absent
            ? missingMembers.Build(method, absent)
            : nativeImportAddresses.Redirect(body);
    }
}
