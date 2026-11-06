using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
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
    IMetadataStackTypeResolver stackTypes,
    IBaseTypeIdentityResolver baseTypes) : IMetadataMethodReferenceResolver
{
    // A token resolves the same way in the same generic context every time it is met, and
    // every call site naming it meets it again; a resolution is made once per compile.
    private readonly ConcurrentDictionary<ResolutionKey, MethodInstanceModel> _resolved = new();

    public MethodInstanceModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset,
        CliGenericContext? genericContext = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var context = (genericContext ?? CliGenericContext.Empty).Normalize();
        var key = new ResolutionKey(source.Identity.Name, metadataToken, context.TypeArguments, context.MethodArguments);
        if (_resolved.TryGetValue(key, out var known))
            return known;
        return _resolved.GetOrAdd(key, Resolve(source, metadataToken, methodDisplayName, ilOffset, context));
    }

    private MethodInstanceModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset,
        CliGenericContext context)
    {
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
        HashSet<EntityKey>? visited = null;
        MethodDefinitionModel[] matches;
        while (true)
        {
            matches = Matching(declaringDefinition, name, referenceSignature.GenericParameterCount,
                typeArguments, expected);
            if (matches.Length != 0 || name is ".ctor" or ".cctor")
                break;
            visited ??= [];
            if (!visited.Add(declaringDefinition.Key))
                break;
            var baseIdentity = baseTypes.GetBaseTypeIdentity(declaringIdentity);
            if (baseIdentity is null)
                break;
            declaringIdentity = baseIdentity;
            declaringDefinition = ResolveTypeIdentity(declaringIdentity);
            typeArguments = declaringIdentity.Shape == CliTypeShape.GenericInstantiation
                ? declaringIdentity.TypeArguments : [];
        }
        if (matches.Length > 1)
        {
            // Overloads such as Vector<T>(ReadOnlySpan<T>) and
            // Vector<T>(ReadOnlySpan<byte>) coincide once T is byte. A member
            // reference names the overload by its declared signature, in which
            // the type's parameters stay parameters (ECMA-335 II.23.2.1).
            var declared = reference.DecodeMethodSignature(
                new SignatureTypeProvider(
                    source.Identity,
                    source.Reader,
                    source.AssemblyIdentityAliases),
                new CliGenericContext([], []));
            matches = matches
                .Where(method => signatureTypes.Compare(
                        method.Signature.ReturnSignatureType,
                        declared.ReturnType) &&
                    method.Signature.ParameterSignatureTypes.Zip(declared.ParameterTypes)
                        .All(pair => signatureTypes.Compare(pair.First, pair.Second)))
                .ToArray();
        }
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

    // The type's methods of this name and arity whose signature, in the type's
    // instantiation, is the expected one.
    private MethodDefinitionModel[] Matching(
        TypeDefinitionModel type,
        string name,
        int genericArity,
        ImmutableArray<CliTypeIdentity> typeArguments,
        MethodSignatureModel expected)
    {
        List<MethodDefinitionModel>? matches = null;
        foreach (var key in type.Methods)
        {
            var method = GetMethod(key);
            if (method.GenericArity != genericArity || !string.Equals(method.Name, name, StringComparison.Ordinal))
                continue;
            var candidate = method.Signature.Substitute(typeArguments);
            if (!signatureTypes.Compare(candidate.ReturnSignatureType, expected.ReturnSignatureType) ||
                candidate.ParameterSignatureTypes.Length != expected.ParameterSignatureTypes.Length)
            {
                continue;
            }
            var same = true;
            for (var index = 0; index < candidate.ParameterSignatureTypes.Length && same; index++)
            {
                same = signatureTypes.Compare(candidate.ParameterSignatureTypes[index],
                    expected.ParameterSignatureTypes[index]);
            }
            if (same) (matches ??= []).Add(method);
        }
        return matches is null ? [] : [.. matches];
    }

    // A resolution's inputs: the token's assembly and the generic context's arguments,
    // compared by reference (identities are shared, so equal arguments are usually the same).
    internal readonly record struct ResolutionKey(
        string Assembly,
        int Token,
        ImmutableArray<CliTypeIdentity> TypeArguments,
        ImmutableArray<CliTypeIdentity> MethodArguments)
    {
        public bool Equals(ResolutionKey other) =>
            Token == other.Token &&
            string.Equals(Assembly, other.Assembly, StringComparison.Ordinal) &&
            TypeArguments.AsSpan().SequenceEqual(other.TypeArguments.AsSpan(), ReferenceEqualityComparer.Instance) &&
            MethodArguments.AsSpan().SequenceEqual(other.MethodArguments.AsSpan(), ReferenceEqualityComparer.Instance);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Token);
            hash.Add(Assembly, StringComparer.Ordinal);
            foreach (var argument in TypeArguments) hash.Add(RuntimeHelpers.GetHashCode(argument));
            hash.Add(-1);
            foreach (var argument in MethodArguments) hash.Add(RuntimeHelpers.GetHashCode(argument));
            return hash.ToHashCode();
        }
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
