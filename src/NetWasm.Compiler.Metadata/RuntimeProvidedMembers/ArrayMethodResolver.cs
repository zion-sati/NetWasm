using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal sealed class ArrayMethodResolver(
    ISignatureTypeComparer signatureTypes) : IRuntimeProvidedMethodResolver
{
    private readonly ISignatureTypeComparer _signatureTypes = signatureTypes ??
        throw new ArgumentNullException(nameof(signatureTypes));

    public MethodInstanceModel? Resolve(RuntimeProvidedMethodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeclaringType.Shape != CliTypeShape.Array)
        {
            return null;
        }

        var elementType = request.DeclaringType.ElementType!;
        if (!request.IsInstance || !HasValidSignature(request, elementType))
        {
            throw Unsupported(request);
        }

        var definition = new MethodDefinitionModel(
            new EntityKey(request.SourceAssembly, request.MethodToken),
            new EntityKey(request.SourceAssembly, request.DeclaringTypeToken),
            request.Name,
            IsStatic: false,
            request.Signature,
            RelativeVirtualAddress: 0);
        return new MethodInstanceModel(definition, request.DeclaringType, [], request.Signature);
    }

    private bool HasValidSignature(
        RuntimeProvidedMethodRequest request,
        CliTypeIdentity elementType)
    {
        var signature = request.Signature;
        var rank = request.DeclaringType.ArrayRank;
        var indexCount = request.Name == "Set" ? rank : signature.ParameterSignatureTypes.Length;
        if (indexCount != rank ||
            signature.ParameterSignatureTypes.Take(rank).Any(type =>
                !_signatureTypes.Compare(type, CliTypeIdentity.Primitive("i4", CliValueKind.I4))))
        {
            return false;
        }

        return request.Name switch
        {
            ".ctor" => signature.ReturnType == CliValueKind.Void,
            "Get" => _signatureTypes.Compare(signature.ReturnSignatureType, elementType),
            "Address" =>
                signature.ReturnSignatureType.Shape == CliTypeShape.ManagedByReference &&
                _signatureTypes.Compare(signature.ReturnSignatureType.ElementType!, elementType),
            "Set" => signature.ParameterSignatureTypes.Length == rank + 1 &&
                _signatureTypes.Compare(signature.ParameterSignatureTypes[rank], elementType) &&
                signature.ReturnType == CliValueKind.Void,
            _ => false,
        };
    }

    private static CompilerException Unsupported(RuntimeProvidedMethodRequest request) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"runtime-provided array member '{request.DeclaringType}::{request.Name}' " +
            "has an unsupported signature",
            request.MethodDisplayName,
            request.IlOffset));
}
