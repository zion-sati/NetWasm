using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodResolver(
    IMetadataEntityHandleReader entityHandles,
    IMetadataTypeResolver metadataTypes,
    IMethodRepository methods) : IMetadataMethodResolver
{
    public MethodDefinitionModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset)
    {
        var handle = entityHandles.Read(
            metadataToken, "method", methodDisplayName, ilOffset);
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            return source.Methods[metadataToken];
        }
        if (handle.Kind != HandleKind.MemberReference)
        {
            throw UnsupportedToken("method", metadataToken, methodDisplayName, ilOffset);
        }

        var reference = source.Reader.GetMemberReference(
            (MemberReferenceHandle)handle);
        var signature = reference.DecodeMethodSignature(
            new SignatureTypeProvider(
                source.Identity,
                source.Reader,
                source.AssemblyIdentityAliases),
            genericContext: null);
        var declaringType = metadataTypes.Resolve(source, reference.Parent);
        var name = source.Reader.GetString(reference.Name);
        var matches = declaringType.Methods
            .Select(GetMethod)
            .Where(method =>
                method.Name == name &&
                method.Signature.ReturnSignatureType.Equals(signature.ReturnType) &&
                method.Signature.ParameterSignatureTypes.SequenceEqual(signature.ParameterTypes))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.AssemblyResolution,
                    $"method reference '{declaringType.FullName}::{name}' did not resolve uniquely",
                    methodDisplayName,
                    ilOffset));
    }

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
    private MethodDefinitionModel GetMethod(EntityKey key) => methods.GetMethod(key);
}
