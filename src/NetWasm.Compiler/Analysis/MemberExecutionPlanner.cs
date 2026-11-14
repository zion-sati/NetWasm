using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class MemberExecutionPlanner(
    IRuntimeIntrinsicRegistry intrinsics,
    IDelegateTypeRecognizer delegateTypes,
    ITypeFinder typeFinder,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    INullableTypeResolver nullableTypes,
    ITypeDefinitionResolver typeDefinitions) : IMemberExecutionPlanner
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
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));

    public MemberExecutionPlan Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<MethodInstanceModel> methodDescriptors,
        IEnumerable<FieldInstanceModel> fieldDescriptors)
    {
        ArgumentNullException.ThrowIfNull(reachableMethods);
        ArgumentNullException.ThrowIfNull(methodDescriptors);
        ArgumentNullException.ThrowIfNull(fieldDescriptors);
        var demands = reachableMethods
            .Select(method => (method.Definition.Key, Intrinsic: _intrinsics.TryGetIntrinsic(
                    method.Definition.Key,
                    out var intrinsic)
                ? intrinsic
                : (RuntimeIntrinsic?)null))
            .Where(demand => demand.Intrinsic is
                RuntimeIntrinsic.MemberExecuteMethod or
                RuntimeIntrinsic.DelegateDynamicInvoke or
                RuntimeIntrinsic.MemberReadField or
                RuntimeIntrinsic.MemberWriteField)
            .ToArray();
        var demand = demands.Select(item => item.Intrinsic).ToHashSet();
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
                        RequiresDispatch(method),
                        []),
                    StringComparer.Ordinal)
            : ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty;
        var plannedFields = demand.Contains(RuntimeIntrinsic.MemberReadField) ||
            demand.Contains(RuntimeIntrinsic.MemberWriteField)
            ? fieldDescriptors
                .Where(IsSupportedField)
                .DistinctBy(field => field.CanonicalName)
                .OrderBy(field => field.CanonicalName, StringComparer.Ordinal)
                .ToImmutableDictionary(
                    field => field.CanonicalName,
                    StringComparer.Ordinal)
            : ImmutableDictionary<string, FieldInstanceModel>.Empty;
        var delegateInvocation = demand.Contains(RuntimeIntrinsic.DelegateDynamicInvoke)
            ? new DelegateDynamicInvokePlan(
                methodDescriptors
                    .Where(IsSupportedDelegateInvoke)
                    .DistinctBy(method => method.CanonicalName)
                    .OrderBy(method => method.CanonicalName, StringComparer.Ordinal)
                    .ToImmutableDictionary(
                        method => method.CanonicalName,
                        StringComparer.Ordinal),
                demands
                    .Where(item => item.Intrinsic == RuntimeIntrinsic.DelegateDynamicInvoke)
                    .Select(item => item.Key)
                    .ToImmutableHashSet(),
                ResolveSupportTarget("ThrowDynamicInvokeUnsupported", 0),
                ResolveSupportTarget("ThrowDynamicInvokeArgument", 0),
                ResolveSupportTarget("ThrowDynamicInvokeParameterCount", 0),
                ResolveSupportTarget("ThrowTargetInvocation", 1))
            : null;
        var memberSupport = demand.Contains(RuntimeIntrinsic.MemberExecuteMethod) ||
            demand.Contains(RuntimeIntrinsic.MemberReadField) ||
            demand.Contains(RuntimeIntrinsic.MemberWriteField)
                ? ResolveSupportTarget("ThrowUnsupported", 0)
                : null;
        return new(plannedMethods, plannedFields, memberSupport)
        {
            MethodInvokers = demands
                .Where(item => item.Intrinsic == RuntimeIntrinsic.MemberExecuteMethod)
                .Select(item => item.Key).ToImmutableHashSet(),
            DelegateInvocation = delegateInvocation,
        };
    }

    // A value type's methods need no dispatch: the type is sealed, and the call runs on the box's contents.
    internal static bool RequiresDispatch(MethodInstanceModel method) =>
        method.Definition.IsVirtual && !method.DeclaringType.IsValueType;

    // Methods of reference types and of value types (on a box's contents), constructors, static methods; struct
    // values travel boxed. Nullable and by-reference-like owners stay out: a nullable's box is its underlying value's
    // box, and a ref struct has no box.
    private bool IsSupportedMethod(MethodInstanceModel method)
    {
        var definition = method.Definition;
        if (definition.Name == ".cctor" ||
            _intrinsics.TryGetIntrinsic(definition.Key, out _) ||
            _delegateTypes.Recognize(method.DeclaringType) ||
            !method.Signature.ParameterSignatureTypes.All(IsSupportedParameter))
        {
            return false;
        }
        // ".ctor" names only instance constructors; the type initializer is ".cctor".
        if (definition.Name == ".ctor")
        {
            return IsConstructible(method.DeclaringType);
        }
        return (!definition.IsAbstract || definition.IsVirtual) &&
            (definition.IsStatic || IsSupportedOwner(method.DeclaringType)) &&
            (method.Signature.ReturnSignatureType.StackKind == CliValueKind.Void ||
                IsSupportedResult(method.Signature.ReturnSignatureType));
    }

    private bool IsSupportedField(FieldInstanceModel field) =>
        (field.Definition.IsStatic
            ? !field.Definition.IsLiteral && !field.DeclaringType.ContainsGenericParameters
            : IsSupportedOwner(field.DeclaringType)) &&
        IsSupportedResult(field.FieldType);

    private bool IsSupportedOwner(CliTypeIdentity type) =>
        !type.ContainsGenericParameters &&
        (!type.IsValueType || type.StackKind != CliValueKind.ValueType || IsSupportedStruct(type));

    private bool IsConstructible(CliTypeIdentity type)
    {
        if (type.ContainsGenericParameters || type.CanonicalName == "primitive:string" ||
            type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation or CliTypeShape.Primitive))
        {
            return false;
        }
        if (type.IsValueType)
        {
            return IsSupportedStruct(type);
        }
        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        return !definition.IsAbstract && !definition.IsInterface;
    }

    // A closed struct that is neither nullable nor by-reference-like, held boxed.
    private bool IsSupportedStruct(CliTypeIdentity type) =>
        type.StackKind == CliValueKind.ValueType &&
        NamesDefinition(type) &&
        !type.ContainsGenericParameters &&
        _nullableTypes.Resolve(type) is null &&
        !_typeDefinitions.ResolveTypeIdentity(type).IsByRefLike;

    // A named type always carries its assembly and full name.
    private static bool NamesDefinition(CliTypeIdentity type) =>
        (type.Shape == CliTypeShape.GenericInstantiation ? type.ElementType! : type).Shape == CliTypeShape.Named;

    private bool IsSupportedParameter(CliTypeIdentity type) =>
        IsSupportedValue(type) || IsSupportedStruct(type);

    private bool IsSupportedDelegateInvoke(MethodInstanceModel method) =>
        method.Definition.Name == "Invoke" &&
        !method.Definition.IsStatic &&
        !method.DeclaringType.ContainsGenericParameters &&
        _delegateTypes.Recognize(method.DeclaringType) &&
        IsSupportedDelegateResult(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes.All(IsSupportedDelegateParameter);

    private bool IsSupportedDelegateResult(CliTypeIdentity type)
    {
        if (type.StackKind == CliValueKind.Void)
        {
            return true;
        }
        if (_nullableTypes.Resolve(type) is { } underlying)
        {
            return !type.ContainsGenericParameters && underlying.StackKind is
                CliValueKind.I4 or CliValueKind.I8 or
                CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt;
        }
        return IsSupportedDynamicValue(type, allowNullable: true);
    }

    private bool IsSupportedDelegateParameter(CliTypeIdentity type)
    {
        if (type.StackKind != CliValueKind.ManagedAddress)
        {
            return IsSupportedDynamicValue(type, allowNullable: false);
        }
        return type.ElementType is { } element &&
            IsSupportedDynamicValue(element, allowNullable: false);
    }

    private bool IsSupportedDynamicValue(
        CliTypeIdentity type,
        bool allowNullable) =>
        !type.ContainsGenericParameters &&
        (type.StackKind is
            CliValueKind.ManagedReference or
            CliValueKind.I4 or CliValueKind.I8 or
            CliValueKind.F4 or CliValueKind.F8 or
            CliValueKind.NativeInt or CliValueKind.ValueType) &&
        (allowNullable || _nullableTypes.Resolve(type) is null);

    private bool IsSupportedResult(CliTypeIdentity type) =>
        IsSupportedValue(type) ||
        IsSupportedStruct(type) ||
        type.StackKind == CliValueKind.ValueType &&
        !type.ContainsGenericParameters &&
        _nullableTypes.Resolve(type) is
        {
            StackKind:
            CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8
        };

    private static bool IsSupportedValue(CliTypeIdentity type) =>
        type.StackKind is
            CliValueKind.ManagedReference or
            CliValueKind.I4 or CliValueKind.I8 or
            CliValueKind.F4 or CliValueKind.F8;

    private MethodInstanceModel ResolveSupportTarget(string name, int parameterCount)
    {
        var type = _typeFinder.FindType(
            "System.Runtime.CompilerServices.RuntimeMemberExecution");
        var candidates = type.Methods
            .Select(_methods.GetMethod)
            .Where(candidate =>
                candidate.Name == name &&
                candidate.IsStatic &&
                candidate.Signature.ParameterSignatureTypes.Length == parameterCount)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"bounded member execution must define exactly one '{name}' target"));
        }
        var method = candidates[0];
        return _methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            _symbols.Format(method),
            0);
    }
}
