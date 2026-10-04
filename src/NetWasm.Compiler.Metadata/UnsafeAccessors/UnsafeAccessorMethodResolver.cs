using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed class UnsafeAccessorMethodResolver(
    IMethodRepository methods,
    IUnsafeAccessorSignatureReader signatures,
    IUnsafeAccessorSignatureComparer comparer,
    IUnsafeAccessorConstraintReader constraints,
    IUnsafeAccessorConstraintValidator constraintValidator) : IUnsafeAccessorMemberResolver
{
    public UnsafeAccessorMemberMatch Resolve(UnsafeAccessorMemberRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var candidates = new List<MethodDefinitionModel>();
        foreach (var key in request.Owner.Methods)
        {
            var method = methods.GetMethod(key);
            if (method.Name == request.Name &&
                method.IsStatic == (request.Kind == UnsafeAccessorMemberKind.StaticMethod) &&
                Matches(method, includeModifiers: false))
            {
                candidates.Add(method);
            }
        }

        if (candidates.Count == 0)
            return new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.MissingMethod);

        // CLR retries with exact modifiers only after an ambiguous first pass.
        // Zero exact matches after ambiguity is still AmbiguousMatch, not MissingMethod.
        if (candidates.Count > 1)
        {
            candidates = [.. candidates.Where(candidate => Matches(candidate, includeModifiers: true))];
            if (candidates.Count != 1)
                return new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.AmbiguousMatch);
        }

        var target = candidates[0];
        return constraintValidator.Validate(constraints.Read(request.Accessor), constraints.Read(target))
            ? new UnsafeAccessorMemberMatch.Method(target)
            : new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.InvalidProgram);

        bool Matches(MethodDefinitionModel method, bool includeModifiers)
        {
            var targetSignature = signatures.Read(method);
            var isConstructor = request.Kind == UnsafeAccessorMemberKind.Constructor;
            var first = isConstructor ? 0 : 1;
            if (request.Signature.Header.CallingConvention != targetSignature.Header.CallingConvention ||
                request.Signature.GenericParameterCount != targetSignature.GenericParameterCount ||
                request.Signature.ParameterTypes.Length != targetSignature.ParameterTypes.Length + first)
                return false;

            if (isConstructor
                ? targetSignature.ReturnType.StackKind != CliValueKind.Void
                : !comparer.Compare(request.Signature.ReturnType, targetSignature.ReturnType, includeModifiers))
                return false;

            for (var index = 0; index < targetSignature.ParameterTypes.Length; index++)
            {
                if (!comparer.Compare(request.Signature.ParameterTypes[index + first],
                    targetSignature.ParameterTypes[index], includeModifiers))
                    return false;
            }

            return true;
        }
    }
}
