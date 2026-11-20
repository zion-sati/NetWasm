// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ConstrainedCallTargetResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodImplementationResolver methodImplementations) :
    IConstrainedCallTargetResolver
{
    public MethodDefinitionModel Resolve(ConstrainedCallTargetRequest request)
    {
        var explicitCandidates = methodImplementations
            .GetMethodImplementations(ResolveImplementationType(request))
            .Where(mapping => MatchesDeclaration(mapping.Declaration, request))
            .Select(mapping => mapping.Body.Definition)
            .DistinctBy(candidate => candidate.Key)
            .ToArray();
        if (explicitCandidates.Length != 0)
        {
            return RequireUnique(explicitCandidates, request);
        }
        var concreteCandidates = MatchingCandidates(
            request.ConcreteType.Methods,
            request);
        if (concreteCandidates.Length != 0)
        {
            return RequireUnique(concreteCandidates, request);
        }
        var candidateMethods = request.ConcreteType.Methods.Clear();
        if (request.ConcreteType.IsEnum)
        {
            candidateMethods = candidateMethods.AddRange(
                types.FindType("System.Enum").Methods);
        }
        else if (request.ConcreteType.IsValueType &&
                 IsValueTypeFallback(request.Declaration))
        {
            candidateMethods = candidateMethods.AddRange(
                types.FindType("System.ValueType").Methods);
        }
        var namedCandidates = candidateMethods
            .Select(methods.GetMethod)
            .Where(candidate =>
                candidate.Name == request.Declaration.Name &&
                candidate.IsStatic == request.Declaration.IsStatic &&
                candidate.GenericArity == request.Declaration.GenericArity);
        var candidates = namedCandidates
            .Where(candidate => MatchesSignature(candidate, request))
            .DistinctBy(candidate => candidate.Key)
            .ToArray();
        if (candidates.Length == 0 && !request.Declaration.HasBody)
        {
            candidates = namedCandidates
                .Where(candidate => MatchesStackSignature(candidate, request))
                .ToArray();
        }
        if (candidates.Length == 0 &&
            (request.Declaration.HasBody || !request.ConcreteType.IsValueType))
        {
            return request.Declaration;
        }
        return RequireUnique(candidates, request);
    }

    private static bool IsValueTypeFallback(MethodDefinitionModel declaration) =>
        !declaration.IsStatic &&
        declaration.Name is "Equals" or "GetHashCode" or "ToString";

    private MethodDefinitionModel[] MatchingCandidates(
        System.Collections.Immutable.ImmutableArray<EntityKey> candidateMethods,
        ConstrainedCallTargetRequest request)
    {
        // A virtual or interface method is implemented only by a virtual method:
        // C# emits every implicit implementation virtual, so a non-virtual method
        // of the same name only hides it.
        var named = candidateMethods
            .Select(methods.GetMethod)
            .Where(candidate =>
                candidate.Name == request.Declaration.Name &&
                candidate.IsStatic == request.Declaration.IsStatic &&
                candidate.GenericArity == request.Declaration.GenericArity &&
                (candidate.IsStatic || !request.Declaration.IsVirtual || candidate.IsVirtual))
            .ToArray();
        var exact = named
            .Where(candidate => MatchesSignature(candidate, request))
            .DistinctBy(candidate => candidate.Key)
            .ToArray();
        // A declaration with a body is a default implementation, which a type without an exact implementation uses
        // (ECMA-335 II.12.2): matching only stack kinds would take an overload of another signature, such as
        // TryParse(ReadOnlySpan<char>, ...) for INumberBase's default TryParse(ReadOnlySpan<byte>, ...).
        if (exact.Length != 0 || request.Declaration.HasBody)
        {
            return exact;
        }
        return named
            .Where(candidate => MatchesStackSignature(candidate, request))
            .DistinctBy(candidate => candidate.Key)
            .ToArray();
    }

    private static MethodDefinitionModel RequireUnique(
        MethodDefinitionModel[] candidates,
        ConstrainedCallTargetRequest request)
    {
        if (candidates.Length != 1)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"constrained call target '{request.Declaration.Name}' on " +
                $"'{request.ConstrainedType.CanonicalName}' did not resolve uniquely"));
        }
        return candidates[0];
    }

    private static CliTypeIdentity ResolveImplementationType(
        ConstrainedCallTargetRequest request)
    {
        if (request.ConstrainedType.Shape != CliTypeShape.Primitive)
        {
            return request.ConstrainedType;
        }
        var definition = CliTypeIdentity.Named(
            request.ConcreteType.Key.Assembly,
            request.ConcreteType.Namespace,
            request.ConcreteType.Name,
            request.ConcreteType.IsValueType,
            request.ConstrainedType.StackKind);
        return definition;
    }

    private static bool MatchesDeclaration(
        MethodInstanceModel declaration,
        ConstrainedCallTargetRequest request) =>
        declaration.Definition.Key == request.Declaration.Key &&
        MatchesSignature(
            declaration.Signature.Substitute(
                request.TypeArguments,
                request.MethodArguments),
            request.CallSignature);

    private static bool MatchesSignature(
        MethodDefinitionModel candidate,
        ConstrainedCallTargetRequest request) =>
        MatchesSignature(
            candidate.Signature.Substitute(request.TypeArguments, request.MethodArguments),
            request.CallSignature);

    private static bool MatchesSignature(
        MethodSignatureModel candidate,
        MethodSignatureModel call) =>
        candidate.ParameterSignatureTypes.SequenceEqual(call.ParameterSignatureTypes) &&
        candidate.ReturnSignatureType.Equals(call.ReturnSignatureType);

    private static bool MatchesStackSignature(
        MethodDefinitionModel candidate,
        ConstrainedCallTargetRequest request)
    {
        var signature = candidate.Signature.Substitute(
            request.TypeArguments,
            request.MethodArguments);
        return signature.ParameterTypes.SequenceEqual(request.CallSignature.ParameterTypes) &&
               signature.ReturnType == request.CallSignature.ReturnType;
    }
}
