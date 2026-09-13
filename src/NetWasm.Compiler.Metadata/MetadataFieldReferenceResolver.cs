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
    IMetadataStackTypeResolver stackTypes) : IMetadataFieldReferenceResolver
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
        var matches = declaringDefinitionMatch.Fields
            .Select(GetField)
            .Where(field =>
                field.Name == name &&
                field.SignatureType.Substitute(typeArguments).Equals(expected))
            .ToArray();
        var definitionMatch = matches.Length == 1
            ? matches[0]
            : throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.AssemblyResolution,
                    $"field reference '{declaringIdentity}::{name}' did not resolve uniquely",
                    methodDisplayName,
                    ilOffset));
        return new FieldInstanceModel(
            definitionMatch,
            declaringIdentity,
            stackTypes.Resolve(
                definitionMatch.SignatureType,
                new CliGenericContext(typeArguments, [])));
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
