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
    IFrameworkMethodContractValidator contracts,
    IRuntimeTypeDefinitions runtimeDefinitions,
    ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), OriginalContractType> originalContracts,
    IMetadataCrossAssemblyPrivateAccessValidator privateAccess,
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
            var canonicalAssembly = source.AssemblyIdentityAliases.Canonicalize(
                directDeclaringDefinition.Key.Assembly, directDeclaringDefinition.FullName);
            if (canonicalAssembly != directDeclaringDefinition.Key.Assembly)
            {
                var canonicalIdentity = CliTypeIdentity.Named(canonicalAssembly, "",
                    directDeclaringDefinition.FullName, directDeclaringDefinition.IsValueType);
                directDeclaringDefinition = ResolveTypeIdentity(canonicalIdentity);
                var originalDefinition = definition;
                definition = MatchDefinition(directDeclaringDefinition, canonicalIdentity,
                    definition.Name, definition.GenericArity, definition.IsStatic,
                    definition.Signature.Substitute(context.TypeArguments), context.TypeArguments,
                    () => originalDefinition.Signature, methodDisplayName, ilOffset);
                contracts.Validate(new(originalDefinition, definition, methodDisplayName, ilOffset));
            }
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
        // Array member signatures use the caller's generic parameters. Unlike a
        // constructed named type, the array has no independent generic scope.
        var typeArguments = declaringIdentity.Shape switch
        {
            CliTypeShape.GenericInstantiation => declaringIdentity.TypeArguments,
            CliTypeShape.Array => context.TypeArguments,
            _ => [],
        };
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

        // A member reference names its overload by its declared signature, in which the
        // type's parameters stay parameters (ECMA-335 II.23.2.1).
        MethodSignatureModel DeclaredSignature()
        {
            var declared = reference.DecodeMethodSignature(
                new SignatureTypeProvider(
                    source.Identity,
                    source.Reader,
                    source.AssemblyIdentityAliases),
                new CliGenericContext([], []));
            return new(declared.ReturnType, declared.ParameterTypes);
        }

        var declaringDefinition = ResolveTypeIdentity(declaringIdentity);
        var resolved = MatchInheritedDefinition(declaringDefinition, declaringIdentity,
            name, referenceSignature.GenericParameterCount, !referenceSignature.Header.IsInstance,
            expected, typeArguments, DeclaredSignature, methodDisplayName, ilOffset);
        var definitionMatch = resolved.Definition;
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
                    name, referenceSignature.GenericParameterCount, !referenceSignature.Header.IsInstance,
                    expected, typeArguments, DeclaredSignature, methodDisplayName, ilOffset, originalContract.Source);
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
                    name, referenceSignature.GenericParameterCount, !referenceSignature.Header.IsInstance,
                    expected, typeArguments, DeclaredSignature, methodDisplayName, ilOffset);
                contracts.Validate(new(originalDefinition, definitionMatch, methodDisplayName, ilOffset));
            }
        }
        return new MethodInstanceModel(
            definitionMatch,
            resolved.DeclaringType,
            [],
            stackTypes.Resolve(
                definitionMatch.Signature,
                new CliGenericContext(resolved.TypeArguments, [])));
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

    private (MethodDefinitionModel Definition, CliTypeIdentity DeclaringType,
        ImmutableArray<CliTypeIdentity> TypeArguments) MatchInheritedDefinition(
        TypeDefinitionModel declaringDefinition, CliTypeIdentity declaringIdentity,
        string name, int genericArity, bool isStatic, MethodSignatureModel expected,
        ImmutableArray<CliTypeIdentity> typeArguments, Func<MethodSignatureModel> declared,
        string methodDisplayName, int ilOffset)
    {
        HashSet<EntityKey>? visited = null;
        while (true)
        {
            var matches = FindMatches(declaringDefinition, name, genericArity, isStatic,
                expected, typeArguments, declared, null);
            if (matches.Length == 1)
                return (matches[0], declaringIdentity, typeArguments);
            if (matches.Length != 0 || name is ".ctor" or ".cctor")
                break;
            visited ??= [];
            if (!visited.Add(declaringDefinition.Key))
                break;
            var parent = baseTypes.GetBaseTypeIdentity(declaringIdentity);
            if (parent is null)
                break;
            declaringIdentity = parent;
            declaringDefinition = ResolveTypeIdentity(parent);
            typeArguments = parent.Shape == CliTypeShape.GenericInstantiation ? parent.TypeArguments : [];
        }
        // Preserve the existing diagnostic for a missing or ambiguous definition.
        return (MatchDefinition(declaringDefinition, declaringIdentity, name, genericArity,
            isStatic, expected, typeArguments, declared, methodDisplayName, ilOffset),
            declaringIdentity, typeArguments);
    }

    private MethodDefinitionModel[] FindMatches(TypeDefinitionModel declaringDefinition,
        string name, int genericArity, bool isStatic, MethodSignatureModel expected,
        ImmutableArray<CliTypeIdentity> typeArguments, Func<MethodSignatureModel> declared,
        MetadataAssemblySnapshot? originalSource)
    {
        var matches = declaringDefinition.Methods
            .Select(key => originalSource is null ? GetMethod(key) : originalSource.Methods[key.MetadataToken])
            .Where(method => method.Name == name && method.IsStatic == isStatic &&
                method.GenericArity == genericArity)
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
        if (matches.Length > 1)
        {
            // Overloads such as Vector<T>(ReadOnlySpan<T>) and Vector<T>(ReadOnlySpan<byte>)
            // coincide once T is byte; the declared signature tells them apart.
            var signature = declared();
            matches = matches
                .Where(method => signatureTypes.Compare(
                        method.Signature.ReturnSignatureType,
                        signature.ReturnSignatureType) &&
                    method.Signature.ParameterSignatureTypes.Zip(signature.ParameterSignatureTypes)
                        .All(pair => signatureTypes.Compare(pair.First, pair.Second)))
                .ToArray();
        }
        return matches;
    }

    private MethodDefinitionModel MatchDefinition(TypeDefinitionModel declaringDefinition,
        CliTypeIdentity declaringIdentity, string name, int genericArity, bool isStatic,
        MethodSignatureModel expected, ImmutableArray<CliTypeIdentity> typeArguments,
        Func<MethodSignatureModel> declared, string methodDisplayName, int ilOffset,
        MetadataAssemblySnapshot? originalSource = null)
    {
        var matches = FindMatches(declaringDefinition, name, genericArity, isStatic,
            expected, typeArguments, declared, originalSource);
        return matches.Length == 1
            ? matches[0]
            : throw new CompilerException(
                new CompilerDiagnostic(
                    matches.Length == 0 ? DiagnosticCode.MissingApi : DiagnosticCode.AssemblyResolution,
                    $"method reference '{declaringIdentity}::{name}' did not resolve uniquely " +
                    $"({matches.Length} matches; expected {expected.ReturnSignatureType} " +
                    $"({string.Join(", ", expected.ParameterSignatureTypes)}))",
                    methodDisplayName,
                    ilOffset));
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
