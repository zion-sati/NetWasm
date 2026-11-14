using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataFieldReferenceResolver(
    IMetadataEntityHandleReader entityHandles,
    IMetadataSignatureTypeResolver signatureTypeResolver,
    IFieldSignatureContextResolver signatureContexts,
    ITypeRepository types,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    IMetadataStackTypeResolver stackTypes,
    IFrameworkFieldContractValidator contracts,
    IRuntimeTypeDefinitions runtimeDefinitions,
    ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), OriginalContractType> originalContracts,
    IMetadataCrossAssemblyPrivateAccessValidator privateAccess) : IMetadataFieldReferenceResolver
{
    public FieldInstanceModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset,
        CliGenericContext? genericContext = null)
    {
        var context = (genericContext ?? CliGenericContext.Empty).Normalize();
        var handle = entityHandles.Read(
            metadataToken, "field", methodDisplayName, ilOffset);
        if (handle.Kind == HandleKind.FieldDefinition)
        {
            var definition = source.Fields[metadataToken];
            var declaringDefinition = GetTypeDefinition(
                definition.DeclaringType);
            var canonicalAssembly = source.AssemblyIdentityAliases.Canonicalize(
                declaringDefinition.Key.Assembly, declaringDefinition.FullName);
            if (canonicalAssembly != declaringDefinition.Key.Assembly)
            {
                var canonicalIdentity = CliTypeIdentity.Named(canonicalAssembly, "",
                    declaringDefinition.FullName, declaringDefinition.IsValueType);
                declaringDefinition = ResolveTypeIdentity(canonicalIdentity);
                var originalDefinition = definition;
                definition = MatchDefinition(declaringDefinition, canonicalIdentity,
                    definition.Name, definition.IsStatic,
                    definition.SignatureType.Substitute(context.TypeArguments), context.TypeArguments,
                    methodDisplayName, ilOffset);
                contracts.Validate(new(originalDefinition, definition, methodDisplayName, ilOffset));
            }
            var declaringType = GetTypeIdentity(declaringDefinition);
            if (declaringDefinition.GenericArity != 0 &&
                context.TypeArguments.Length == declaringDefinition.GenericArity)
            {
                declaringType = CliTypeIdentity.GenericInstantiation(
                    declaringType,
                    context.TypeArguments);
            }
            return new FieldInstanceModel(
                definition,
                declaringType,
                stackTypes.Resolve(
                    definition.SignatureType,
                    new CliGenericContext(context.TypeArguments, [])));
        }

        if (handle.Kind != HandleKind.MemberReference)
        {
            throw UnsupportedToken("field", metadataToken, methodDisplayName, ilOffset);
        }
        var reference = source.Reader.GetMemberReference(
            (MemberReferenceHandle)handle);
        var declaringIdentity = signatureTypeResolver.Resolve(
            source, reference.Parent, context);
        var declaringDefinitionMatch = ResolveTypeIdentity(declaringIdentity);
        var typeArguments =
            declaringIdentity.Shape == CliTypeShape.GenericInstantiation
                ? declaringIdentity.TypeArguments
                : [];
        var expected = reference.DecodeFieldSignature(
            new SignatureTypeProvider(
                source.Identity,
                source.Reader,
                source.AssemblyIdentityAliases),
            signatureContexts.Resolve(declaringIdentity));
        var name = source.Reader.GetString(reference.Name);
        var definitionMatch = MatchDefinition(declaringDefinitionMatch, declaringIdentity,
            name, null, expected, typeArguments, methodDisplayName, ilOffset);
        privateAccess.Validate(new(source.Identity, definitionMatch.Key, methodDisplayName, ilOffset));
        if (source.AssemblyIdentityAliases.HasTypeForwarders || !originalContracts.IsEmpty)
        {
            var originalIdentity = ReadOriginalDeclaringIdentity(source, reference.Parent, context);
            var originalType = originalIdentity.Shape == CliTypeShape.GenericInstantiation
                ? originalIdentity.ElementType! : originalIdentity;
            if (originalType.Assembly is { } contractAssembly && originalType.FullName is { } contractName &&
                originalContracts.TryGetValue((contractAssembly, contractName), out var originalContract))
            {
                var originalDefinition = MatchDefinition(originalContract.Definition, originalIdentity,
                    name, null, expected, typeArguments, methodDisplayName, ilOffset, originalContract.Source);
                contracts.Validate(new(originalDefinition, definitionMatch, methodDisplayName, ilOffset)
                {
                    OriginalSource = originalContract.Source,
                });
            }
            else if (originalType.Assembly is { } originalAssembly && originalType.FullName is { } fullName &&
                runtimeDefinitions.Defines(originalAssembly, fullName) &&
                source.AssemblyIdentityAliases.Canonicalize(originalAssembly, fullName) != originalAssembly)
            {
                var originalDefinition = MatchDefinition(ResolveTypeIdentity(originalType), originalIdentity,
                    name, null, expected, typeArguments, methodDisplayName, ilOffset);
                contracts.Validate(new(originalDefinition, definitionMatch, methodDisplayName, ilOffset));
            }
        }
        return new FieldInstanceModel(
            definitionMatch,
            declaringIdentity,
            stackTypes.Resolve(
                definitionMatch.SignatureType,
                new CliGenericContext(typeArguments, [])));
    }

    private static CliTypeIdentity ReadOriginalDeclaringIdentity(MetadataAssemblySnapshot source,
        EntityHandle handle, CliGenericContext context)
    {
        // Decode identity only: a metadata-only facade need not have executable types.
        // The parent already resolved as a type, so it is a definition, reference or specification.
        var provider = new SignatureTypeProvider(source.Identity, source.Reader);
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(source.Reader, (TypeDefinitionHandle)handle, 0x12),
            HandleKind.TypeReference => provider.GetTypeFromReference(source.Reader, (TypeReferenceHandle)handle, 0x12),
            _ => provider.GetTypeFromSpecification(source.Reader, context, (TypeSpecificationHandle)handle, 0x12),
        };
    }

    private FieldDefinitionModel MatchDefinition(TypeDefinitionModel declaringDefinitionMatch,
        CliTypeIdentity declaringIdentity, string name, bool? isStatic,
        CliTypeIdentity expected, ImmutableArray<CliTypeIdentity> typeArguments,
        string methodDisplayName, int ilOffset, MetadataAssemblySnapshot? originalSource = null)
    {
        var matches = declaringDefinitionMatch.Fields
            .Select(key => originalSource is null ? GetField(key) : originalSource.Fields[key.MetadataToken])
            .Where(field =>
                field.Name == name && (isStatic is null || field.IsStatic == isStatic) &&
                field.SignatureType.Substitute(typeArguments).Equals(expected))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new CompilerException(
                new CompilerDiagnostic(
                    matches.Length == 0 ? DiagnosticCode.MissingApi : DiagnosticCode.AssemblyResolution,
                    $"field reference '{declaringIdentity}::{name}' did not resolve uniquely",
                    methodDisplayName,
                    ilOffset));
    }

    private static CliTypeIdentity GetTypeIdentity(TypeDefinitionModel type) =>
        CliTypeIdentity.FromDefinition(type);

    private TypeDefinitionModel GetTypeDefinition(EntityKey key) => types.GetTypeDefinition(key);

    private TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
        typeDefinitions.ResolveTypeIdentity(identity);

    private FieldDefinitionModel GetField(EntityKey key) => fields.GetField(key);

    private static CompilerException UnsupportedToken(
        string kind,
        int token,
        string method,
        int ilOffset) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"unsupported {kind} token 0x{token:x8}",
            method,
            ilOffset));
}
