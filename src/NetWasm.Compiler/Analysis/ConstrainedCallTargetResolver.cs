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
        if (candidates.Length == 0)
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
        var named = candidateMethods
            .Select(methods.GetMethod)
            .Where(candidate =>
                candidate.Name == request.Declaration.Name &&
                candidate.IsStatic == request.Declaration.IsStatic &&
                candidate.GenericArity == request.Declaration.GenericArity)
            .ToArray();
        var exact = named
            .Where(candidate => MatchesSignature(candidate, request))
            .DistinctBy(candidate => candidate.Key)
            .ToArray();
        return exact.Length != 0
            ? exact
            : named
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
