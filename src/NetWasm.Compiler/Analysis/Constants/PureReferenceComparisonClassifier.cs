using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class PureReferenceComparisonClassifier(
    ICalleeMethodBodyReader methodBodies,
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodRewriteRule returnEpilogues,
    IRuntimeIntrinsicRegistry intrinsics) : IPureReferenceComparisonClassifier
{
    private readonly ICalleeMethodBodyReader _methodBodies = methodBodies ?? throw new ArgumentNullException(nameof(methodBodies));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IMethodRewriteRule _returnEpilogues = returnEpilogues ?? throw new ArgumentNullException(nameof(returnEpilogues));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public ReferenceComparison? Classify(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inverted = false;
        while (visited.Add(method.CanonicalName))
        {
            if (!method.Definition.IsStatic || method.Definition.IsVirtual ||
                method.Definition.IsNoInlining || !method.Definition.HasBody ||
                method.Definition.JSImport is not null || method.Definition.WitImport is not null || method.Definition.NativeImport is not null ||
                _intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
                method.Signature.ReturnType != CliValueKind.I4 ||
                method.Signature.ParameterTypes is not [CliValueKind.ManagedReference, CliValueKind.ManagedReference] ||
                // A beforefieldinit owner initializes on static field access, which the
                // comparison grammar below excludes; calling the method does not trigger it.
                _typeDefinitions.ResolveTypeIdentity(method.DeclaringType) is { IsBeforeFieldInit: false } owner &&
                    owner.Methods.Any(key => _methods.GetMethod(key).Name == ".cctor"))
            {
                return null;
            }

            if (!_methodBodies.TryReadMethodBody(method, out var body) || !body.ExceptionRegions.IsEmpty)
            {
                return null;
            }

            var operations = _returnEpilogues.Rewrite(body).Instructions
                .Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            var returned = Array.FindIndex(operations, instruction => instruction.Operation == CilOperation.Return);
            if (returned >= 0)
            {
                // A proven straight-line prefix ending in return cannot execute a
                // redundant Debug-CIL epilogue after that return. Any branch or
                // effect in the prefix still fails the grammar below.
                operations = operations[..(returned + 1)];
            }
            if (operations.Length is not (4 or 6) ||
                operations[0] is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 or 1 } first } ||
                operations[1] is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 or 1 } second } ||
                first.Value == second.Value || operations[^1].Operation != CilOperation.Return)
            {
                return null;
            }

            if (operations.Length == 6)
            {
                if (operations[3] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 0 } } ||
                    operations[4].Operation != CilOperation.CompareEqual)
                {
                    return null;
                }
                inverted = !inverted;
            }

            if (operations[2].Operation == CilOperation.CompareEqual)
            {
                return inverted ? ReferenceComparison.NotEqual : ReferenceComparison.Equal;
            }

            if (operations[2].Operation != CilOperation.Call ||
                _calledMethods.Resolve(operations[2]) is not { } forwarded)
            {
                return null;
            }

            method = forwarded;
        }

        // A recursive call chain is not a proven finite, effect-free comparison.
        return null;
    }
}
