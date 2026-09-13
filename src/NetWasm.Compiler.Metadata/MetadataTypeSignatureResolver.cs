using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeSignatureResolver(
    IMetadataSignatureTypeResolver signatureTypes,
    IMetadataStackTypeResolver stackTypes) : IMetadataTypeSignatureResolver
{
    public CliTypeIdentity Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        CliGenericContext? genericContext = null)
    {
        var handle = MetadataTokens.EntityHandle(metadataToken);
        if (handle.Kind is not (
            HandleKind.TypeDefinition or
            HandleKind.TypeReference or
            HandleKind.TypeSpecification))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"unsupported type token 0x{metadataToken:x8}"));
        }
        var resolvedType = signatureTypes.Resolve(
            source,
            handle,
            genericContext ?? CliGenericContext.Empty);
        return stackTypes.Resolve(resolvedType, genericContext ?? CliGenericContext.Empty);
    }
}
