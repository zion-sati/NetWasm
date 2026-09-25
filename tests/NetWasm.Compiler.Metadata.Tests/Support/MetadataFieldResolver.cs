using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataFieldResolver(
    IMetadataEntityHandleReader entityHandles,
    IMetadataTypeResolver metadataTypes,
    IFieldRepository fields) : IMetadataFieldResolver
{
    public FieldDefinitionModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset)
    {
        var handle = entityHandles.Read(
            metadataToken, "field", methodDisplayName, ilOffset);
        if (handle.Kind == HandleKind.FieldDefinition)
        {
            return source.Fields[metadataToken];
        }
        if (handle.Kind != HandleKind.MemberReference)
        {
            throw UnsupportedToken("field", metadataToken, methodDisplayName, ilOffset);
        }

        var reference = source.Reader.GetMemberReference(
            (MemberReferenceHandle)handle);
        var fieldType = reference.DecodeFieldSignature(
            new SignatureTypeProvider(
                source.Identity,
                source.Reader,
                source.AssemblyIdentityAliases),
            genericContext: null);
        var declaringType = metadataTypes.Resolve(source, reference.Parent);
        var name = source.Reader.GetString(reference.Name);
        var matches = declaringType.Fields
            .Select(GetField)
            .Where(field => field.Name == name && field.SignatureType.Equals(fieldType))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.AssemblyResolution,
                    $"field reference '{declaringType.FullName}::{name}' did not resolve uniquely",
                    methodDisplayName,
                    ilOffset));
    }

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
