using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class MemberExecutionPlanner(
    IRuntimeIntrinsicRegistry intrinsics,
    IDelegateTypeRecognizer delegateTypes,
    ITypeFinder typeFinder,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : IMemberExecutionPlanner
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly IDelegateTypeRecognizer _delegateTypes = delegateTypes ??
        throw new ArgumentNullException(nameof(delegateTypes));
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));
    private readonly IMethodRepository _methods = methods ??
        throw new ArgumentNullException(nameof(methods));
    private readonly IMethodInstanceResolver _methodInstances = methodInstances ??
        throw new ArgumentNullException(nameof(methodInstances));
    private readonly ISymbolFormatter _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));

    public MemberExecutionPlan Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<MethodInstanceModel> methodDescriptors,
        IEnumerable<FieldInstanceModel> fieldDescriptors)
    {
        ArgumentNullException.ThrowIfNull(reachableMethods);
        ArgumentNullException.ThrowIfNull(methodDescriptors);
        ArgumentNullException.ThrowIfNull(fieldDescriptors);
        var demand = reachableMethods
            .Select(method => _intrinsics.TryGetIntrinsic(
                    method.Definition.Key,
                    out var intrinsic)
                ? intrinsic
                : (RuntimeIntrinsic?)null)
            .Where(intrinsic => intrinsic is
                RuntimeIntrinsic.MemberExecuteMethod or
                RuntimeIntrinsic.MemberReadField)
            .ToHashSet();
        if (demand.Count == 0)
        {
            return MemberExecutionPlan.Empty;
        }

        var plannedMethods = demand.Contains(RuntimeIntrinsic.MemberExecuteMethod)
            ? methodDescriptors
                .Where(IsSupportedMethod)
                .DistinctBy(method => method.CanonicalName)
                .OrderBy(method => method.CanonicalName, StringComparer.Ordinal)
                .ToImmutableDictionary(
                    method => method.CanonicalName,
                    method => new MemberMethodExecutionPlan(
                        method,
                        method.Definition.IsVirtual,
                        []),
                    StringComparer.Ordinal)
            : ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty;
        var plannedFields = demand.Contains(RuntimeIntrinsic.MemberReadField)
            ? fieldDescriptors
                .Where(IsSupportedField)
                .DistinctBy(field => field.CanonicalName)
                .OrderBy(field => field.CanonicalName, StringComparer.Ordinal)
                .ToImmutableDictionary(
                    field => field.CanonicalName,
                    StringComparer.Ordinal)
            : ImmutableDictionary<string, FieldInstanceModel>.Empty;
        return new(plannedMethods, plannedFields, ResolveUnsupportedTarget());
    }

    private bool IsSupportedMethod(MethodInstanceModel method) =>
        (!method.Definition.IsAbstract || method.Definition.IsVirtual) &&
        (method.Definition.IsStatic || !method.DeclaringType.IsValueType) &&
        !_delegateTypes.Recognize(method.DeclaringType) &&
        method.Signature.ParameterSignatureTypes.All(IsSupportedValue) &&
        !_intrinsics.TryGetIntrinsic(method.Definition.Key, out _) &&
        IsSupportedValue(method.Signature.ReturnSignatureType);

    private static bool IsSupportedField(FieldInstanceModel field) =>
        !field.Definition.IsStatic &&
        !field.DeclaringType.IsValueType &&
        IsSupportedValue(field.FieldType);

    private static bool IsSupportedValue(CliTypeIdentity type) =>
        type.StackKind is
            CliValueKind.ManagedReference or
            CliValueKind.I4 or CliValueKind.I8 or
            CliValueKind.F4 or CliValueKind.F8;

    private MethodInstanceModel ResolveUnsupportedTarget()
    {
        var type = _typeFinder.FindType(
            "System.Runtime.CompilerServices.RuntimeMemberExecution");
        var candidates = type.Methods
            .Select(_methods.GetMethod)
            .Where(candidate =>
                candidate.Name == "ThrowUnsupported" &&
                candidate.IsStatic &&
                candidate.Signature.ParameterSignatureTypes.IsEmpty)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "bounded member execution must define exactly one unsupported target"));
        }
        var method = candidates[0];
        return _methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            _symbols.Format(method),
            0);
    }
}
