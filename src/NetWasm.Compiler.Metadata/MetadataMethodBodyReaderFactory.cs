// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeImports;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataMethodBodyReaderFactory(
    IMetadataCompilationMaterializationFactory materializations,
    ITypeDefinitionResolverFactory definitions,
    ITypeRepositoryFactory types,
    IFieldRepositoryFactory fields,
    IMethodRepositoryFactory methods,
    ISymbolFormatterFactory symbols) : IMethodBodyReaderFactory
{
    private readonly IMetadataCompilationMaterializationFactory _materializations =
        materializations ?? throw new ArgumentNullException(nameof(materializations));
    private readonly ITypeDefinitionResolverFactory _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ITypeRepositoryFactory _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepositoryFactory _fields =
        fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepositoryFactory _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly ISymbolFormatterFactory _symbols =
        symbols ?? throw new ArgumentNullException(nameof(symbols));

    public IMethodBodyReader Create(MetadataCompilationSnapshot snapshot, bool wholeProgramSuspension = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        var typeDefinitions = _definitions.Create(snapshot);
        var types = _types.Create(snapshot);
        var methods = _methods.Create(snapshot);
        var fields = _fields.Create(snapshot);
        var signatureTypes = new MetadataSignatureTypeResolver(
            new MetadataTypeResolver(typeDefinitions),
            new MetadataTypeIdentityResolver(types));
        var signatureComparer = new SignatureTypeComparer();
        var stackTypes = new MetadataStackTypeResolver(typeDefinitions);
        var methodReferences = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(),
            signatureTypes,
            signatureComparer,
            types,
            typeDefinitions,
            methods,
            new ArrayMethodResolver(signatureComparer),
            stackTypes,
            new FrameworkMethodContractValidator(new MetadataAssemblyResolver(
                materialized.MetadataAssemblies, ImmutableDictionary<string, string>.Empty, availability)),
            new RuntimeTypeDefinitions(materialized.MetadataAssemblies),
            materialized.OriginalContractTypes,
            new MetadataCrossAssemblyPrivateAccessValidator(new MetadataAssemblyResolver(
                materialized.MetadataAssemblies, ImmutableDictionary<string, string>.Empty, availability)),
            new MetadataBaseTypeIdentityResolver(typeDefinitions,
                new MetadataAssemblyResolver(materialized.MetadataAssemblies,
                    materialized.ReferenceAssemblyAliases, availability), signatureTypes));
        var symbols = _symbols.Create(snapshot);
        var methodBodies = new MetadataMethodBodyBlockReader(symbols);
        var fieldReferences = new MetadataFieldReferenceResolver(
            new MetadataEntityHandleReader(),
            signatureTypes,
            new FieldSignatureContextResolver(),
            types,
            typeDefinitions,
            fields,
            stackTypes,
            new FrameworkFieldContractValidator(new MetadataAssemblyResolver(
                materialized.MetadataAssemblies, ImmutableDictionary<string, string>.Empty, availability)),
            new RuntimeTypeDefinitions(materialized.MetadataAssemblies),
            materialized.OriginalContractTypes,
            new MetadataCrossAssemblyPrivateAccessValidator(new MetadataAssemblyResolver(
                materialized.MetadataAssemblies, ImmutableDictionary<string, string>.Empty, availability)));
        var decoders = new CilDecoderFactory(
            methodBodies,
            symbols,
            methodReferences,
            new MetadataTypeEntityResolver(signatureTypes, typeDefinitions),
            fieldReferences,
            new MetadataTypeSignatureResolver(
                signatureTypes,
                stackTypes),
            new MetadataCallSiteSignatureResolver(),
            stackTypes,
            new TypedReferenceCallResolver(new MetadataTypeFinderFactory(_materializations), snapshot, methods, stackTypes));
        var assemblies = new ManagedAssemblyResolver(
            materialized.Assemblies.ToImmutableDictionary(
                assembly => assembly.Identity.Name,
                StringComparer.Ordinal),
            materialized.ReferenceAssemblyAliases,
            availability);
        var runtimeAsyncSources = new RuntimeAsyncSourceReader(decoders, assemblies,
            new CachedStateMachineBodyBuilder(new StateMachineBodyBuilder(decoders, assemblies, methods,
                new StateMachineRewriter(
                    () => new RuntimeAsyncMemberResolver(new MetadataTypeFinderFactory(_materializations), snapshot,
                        methods, fields, typeDefinitions, stackTypes),
                    fields,
                    new CilBodyLayout(),
                    stackTypes))),
            new CachedStateMachineBodyBuilder(new AsyncIteratorBodyBuilder(decoders, assemblies, methods,
                new AsyncIteratorRewriter(
                    () => new RuntimeAsyncMemberResolver(new MetadataTypeFinderFactory(_materializations), snapshot,
                        methods, fields, typeDefinitions, stackTypes),
                    fields,
                    new CilBodyLayout()),
                () => new RuntimeAsyncMemberResolver(new MetadataTypeFinderFactory(_materializations), snapshot,
                    methods, fields, typeDefinitions, stackTypes),
                new CilBodyLayout())));
        // A compile whose analysis stays within each method (the early cutoff's) lowers every await as one that may
        // suspend; any other knows which lowered methods cannot (RuntimeAsyncSuspensionFacts).
        IRuntimeAsyncSuspensionFacts suspensions = wholeProgramSuspension
            ? new RuntimeAsyncSuspensionFacts(runtimeAsyncSources,
                () => new RuntimeAsyncMemberResolver(new MetadataTypeFinderFactory(_materializations), snapshot, methods,
                    fields, typeDefinitions, stackTypes))
            : new AnyRuntimeAsyncMethodMaySuspend();
        // One resolver serves the lowering's builders, as each made its own before.
        Func<IRuntimeAsyncMemberResolver> runtimeAsyncMembers = () => new RuntimeAsyncMemberResolver(
            new MetadataTypeFinderFactory(_materializations), snapshot, methods, fields, typeDefinitions, stackTypes);
        var catches = new RuntimeAsyncCatchEmitter(runtimeAsyncMembers);
        // Bodies are separated where the whole-program facts fold the context question, as the suspension facts are, and
        // only in a program that refers to no API changing an ambient context, where the question folds to false.
        IRuntimeAsyncBodyPlacement bodyPlacement = wholeProgramSuspension &&
            !new AmbientContextWriterFinder().Finds(snapshot)
                ? new SeparateRuntimeAsyncBodies()
                : new NoSeparateRuntimeAsyncBodies();
        return new MetadataMethodBodyReader(
            decoders,
            assemblies,
            methodReferences,
            symbols,
            new UnsafeAccessorTargetResolverFactory(materialized, typeDefinitions, types, methods, fields, stackTypes),
            new UnsafeAccessorBodyBuilder(),
            new DelegateAsynchronousCallBodyBuilder(new MetadataTypeFinderFactory(_materializations), snapshot, methods),
            new UnsafeIntrinsicBodyBuilder(),
            new UnboundNativeImportBodyBuilder(new MetadataTypeFinderFactory(_materializations), snapshot, methods),
            new NativeMarshallingStubBodyBuilder(methods, () => new NativeMarshallingClassifier(
                typeDefinitions,
                new MetadataBaseTypeIdentityResolver(typeDefinitions,
                    new MetadataAssemblyResolver(materialized.MetadataAssemblies,
                        materialized.ReferenceAssemblyAliases, availability), signatureTypes),
                methods,
                fields,
                new NativeMarshallingMembers(new MetadataTypeFinderFactory(_materializations), snapshot, methods, fields),
                new NativeStructCopierFactory(new CompilationNativeStructSource(typeDefinitions, fields))),
                new CilBodyLayout()),
            new MissingMemberBodyBuilder(new MetadataTypeFinderFactory(_materializations), snapshot, methods),
            new MissingTypeFinder(typeDefinitions),
            new RuntimeAsyncBodyBuilder(
                runtimeAsyncMembers,
                () => new RuntimeAsyncFrameLayoutResolver(new MetadataTypeFinderFactory(_materializations), snapshot, fields,
                    typeDefinitions),
                typeDefinitions,
                new CilBodyLayout(),
                symbols,
                methods,
                fields,
                stackTypes,
                suspensions,
                new RuntimeAsyncActivationGuardSelector(new SelfGuardedActivation(runtimeAsyncMembers, catches),
                    new EntryGuardedBody()),
                new RuntimeAsyncDirectSiteEmitter(runtimeAsyncMembers, methods, bodyPlacement)),
            new RuntimeAsyncResumerBodyBuilder(methods),
            new RuntimeAsyncEntryBuilder(runtimeAsyncMembers, methods, new CilBodyLayout(), catches, symbols),
            runtimeAsyncSources,
            methods,
            bodyPlacement,
            new NativeImportForwarderBodyBuilder(methods),
            new NativeImportAddressRedirector(methods));
    }
}
