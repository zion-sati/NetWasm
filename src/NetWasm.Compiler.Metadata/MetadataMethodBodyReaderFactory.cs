using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

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

    public IMethodBodyReader Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var materialized = _materializations.Create(snapshot);
        var availability = new MetadataAvailabilityValidator(new MetadataLifetime());
        var typeDefinitions = _definitions.Create(snapshot);
        var types = _types.Create(snapshot);
        var methods = _methods.Create(snapshot);
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
            stackTypes);
        var symbols = _symbols.Create(snapshot);
        var methodBodies = new MetadataMethodBodyBlockReader(symbols);
        var fieldReferences = new MetadataFieldReferenceResolver(
            new MetadataEntityHandleReader(),
            signatureTypes,
            new FieldSignatureContextResolver(),
            types,
            typeDefinitions,
            _fields.Create(snapshot),
            stackTypes);
        var decoders = new CilDecoderFactory(
            methodBodies,
            symbols,
            methodReferences,
            new MetadataTypeEntityResolver(new MetadataTypeResolver(typeDefinitions)),
            fieldReferences,
            new MetadataTypeSignatureResolver(
                signatureTypes,
                stackTypes),
            new MetadataCallSiteSignatureResolver(),
            stackTypes);
        return new MetadataMethodBodyReader(
            decoders,
            new ManagedAssemblyResolver(
                materialized.Assemblies.ToImmutableDictionary(
                    assembly => assembly.Identity.Name,
                    StringComparer.Ordinal),
                materialized.ReferenceAssemblyAliases,
                availability));
    }
}
