using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodReferenceResolver(
    IMetadataEntityHandleReader entityHandles,
    IMetadataSignatureTypeResolver signatureTypeResolver,
    ISignatureTypeComparer signatureTypes,
    ITypeRepository types,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IRuntimeProvidedMethodResolver runtimeProvidedMethods,
    IMetadataStackTypeResolver stackTypes) : IMetadataMethodReferenceResolver
{
    public MethodInstanceModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset,
        CliGenericContext? genericContext = null)
    {
        var context = (genericContext ?? CliGenericContext.Empty).Normalize();
        var handle = entityHandles.Read(
            metadataToken, "method", methodDisplayName, ilOffset);
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            var specification = source.Reader.GetMethodSpecification(
                (MethodSpecificationHandle)handle);
            var genericMethod = Resolve(
                source,
                MetadataTokens.GetToken(specification.Method),
                methodDisplayName,
                ilOffset,
                context);
            var methodArguments =
                specification.DecodeSignature(
                    new SignatureTypeProvider(
                        source.Identity,
                        source.Reader,
                        source.AssemblyIdentityAliases),
                    context);
            var declaringTypeArguments =
                genericMethod.DeclaringType.Shape == CliTypeShape.GenericInstantiation
                    ? genericMethod.DeclaringType.TypeArguments
                    : [];
            return genericMethod with
            {
                MethodArguments = methodArguments,
                Signature = stackTypes.Resolve(
                    genericMethod.Definition.Signature,
                    new CliGenericContext(declaringTypeArguments, methodArguments)),
            };
        }

        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var definition = source.Methods[metadataToken];
            var directDeclaringDefinition = GetTypeDefinition(
                definition.DeclaringType);
            var declaringType = GetTypeIdentity(directDeclaringDefinition);
            if (directDeclaringDefinition.GenericArity != 0 &&
                context.TypeArguments.Length == directDeclaringDefinition.GenericArity)
            {
                declaringType = CliTypeIdentity.GenericInstantiation(
                    declaringType,
                    context.TypeArguments);
            }
            var methodArguments = definition.GenericArity == context.MethodArguments.Length
                ? context.MethodArguments
                : [];
            return new MethodInstanceModel(
                definition,
                declaringType,
                methodArguments,
                stackTypes.Resolve(
                    definition.Signature,
                    new CliGenericContext(context.TypeArguments, methodArguments)));
        }

        if (handle.Kind != HandleKind.MemberReference)
        {
            throw UnsupportedToken("method", metadataToken, methodDisplayName, ilOffset);
        }

        var reference = source.Reader.GetMemberReference(
            (MemberReferenceHandle)handle);
        var declaringIdentity = signatureTypeResolver.Resolve(
            source, reference.Parent, context);
        var typeArguments =
            declaringIdentity.Shape == CliTypeShape.GenericInstantiation
                ? declaringIdentity.TypeArguments
                : [];
        var referenceSignature =
            reference.DecodeMethodSignature(
                new SignatureTypeProvider(
                    source.Identity,
                    source.Reader,
                    source.AssemblyIdentityAliases),
                new CliGenericContext(typeArguments, []));
        var expected = new MethodSignatureModel(
            referenceSignature.ReturnType,
            referenceSignature.ParameterTypes).Substitute(
                typeArguments,
                referenceSignature.GenericParameterCount == 0
                    ? context.MethodArguments
                    : []);
        var name = source.Reader.GetString(reference.Name);
        var runtimeProvided = runtimeProvidedMethods.Resolve(new(
            source.Identity,
            metadataToken,
            MetadataTokens.GetToken(reference.Parent),
            declaringIdentity,
            name,
            referenceSignature.Header.IsInstance,
            expected,
            methodDisplayName,
            ilOffset));
        if (runtimeProvided is not null)
        {
            return runtimeProvided;
        }

        var declaringDefinition = ResolveTypeIdentity(declaringIdentity);
        var matches = declaringDefinition.Methods
            .Select(GetMethod)
            .Where(method => method.Name == name &&
                method.GenericArity == referenceSignature.GenericParameterCount)
            .Where(method =>
            {
                var candidate = method.Signature.Substitute(typeArguments);
                return signatureTypes.Compare(
                           candidate.ReturnSignatureType,
                           expected.ReturnSignatureType) &&
                       candidate.ParameterSignatureTypes.Length ==
                           expected.ParameterSignatureTypes.Length &&
                       candidate.ParameterSignatureTypes.Zip(
                               expected.ParameterSignatureTypes)
                           .All(pair => signatureTypes.Compare(
                               pair.First,
                               pair.Second));
            })
            .ToArray();
        var definitionMatch = matches.Length == 1
            ? matches[0]
            : throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.AssemblyResolution,
                    $"method reference '{declaringIdentity}::{name}' did not resolve uniquely " +
                    $"({matches.Length} matches; expected {expected.ReturnSignatureType} " +
                    $"({string.Join(", ", expected.ParameterSignatureTypes)}))",
                    methodDisplayName,
                    ilOffset));
        return new MethodInstanceModel(
            definitionMatch,
            declaringIdentity,
            [],
            stackTypes.Resolve(
                definitionMatch.Signature,
                new CliGenericContext(typeArguments, [])));
    }

    private static CliTypeIdentity GetTypeIdentity(TypeDefinitionModel type) =>
        CliTypeIdentity.FromDefinition(type);

    private TypeDefinitionModel GetTypeDefinition(EntityKey key) => types.GetTypeDefinition(key);

    private TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
        typeDefinitions.ResolveTypeIdentity(identity);

    private MethodDefinitionModel GetMethod(EntityKey key) => methods.GetMethod(key);

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
