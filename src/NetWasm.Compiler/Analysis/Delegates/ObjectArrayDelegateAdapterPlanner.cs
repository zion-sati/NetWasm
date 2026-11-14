using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Delegates;

internal sealed class ObjectArrayDelegateAdapterPlanner(
    IRuntimeIntrinsicRegistry intrinsics,
    IDelegateTypeRecognizer delegateTypes,
    ITypeFinder typeFinder,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : IObjectArrayDelegateAdapterPlanner
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));
    private readonly IDelegateTypeRecognizer _delegateTypes = delegateTypes ??
        throw new ArgumentNullException(nameof(delegateTypes));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMethodRepository _methods = methods ??
        throw new ArgumentNullException(nameof(methods));
    private readonly IMethodInstanceResolver _methodInstances = methodInstances ??
        throw new ArgumentNullException(nameof(methodInstances));
    private readonly ISymbolFormatter _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));

    public ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan> Plan(
        IEnumerable<MethodInstanceModel> reachableMethods)
    {
        ArgumentNullException.ThrowIfNull(reachableMethods);
        var plans = ImmutableDictionary.CreateBuilder<
            string,
            ObjectArrayDelegateAdapterPlan>(StringComparer.Ordinal);
        foreach (var factory in reachableMethods
                     .Where(IsFactory)
                     .OrderBy(method => method.CanonicalName, StringComparer.Ordinal))
        {
            plans.Add(factory.CanonicalName, Build(factory));
        }
        return plans.ToImmutable();
    }

    private bool IsFactory(MethodInstanceModel method) =>
        _intrinsics.TryGetIntrinsic(method.Definition.Key, out var intrinsic) &&
        intrinsic == RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate;

    private ObjectArrayDelegateAdapterPlan Build(MethodInstanceModel factory)
    {
        if (factory.MethodArguments.Length != 1)
        {
            throw RuntimeContract(
                factory,
                "delegate adapter factory must have one closed delegate argument");
        }

        var delegateType = factory.MethodArguments[0];
        if (delegateType.ContainsGenericParameters)
        {
            throw RuntimeContract(
                factory,
                $"delegate adapter argument '{delegateType.CanonicalName}' is not closed");
        }
        if (!_delegateTypes.Recognize(delegateType) ||
            delegateType.FullName is "System.Delegate" or "System.MulticastDelegate")
        {
            return new(
                factory,
                delegateType,
                Invoke: null,
                ResolveThrowAdapter(factory, "ThrowInvalidDelegate"),
                IsSupported: false);
        }

        var invoke = ResolveInvoke(factory, delegateType);
        var parameters = invoke.Signature.ParameterSignatureTypes;
        var returnsVoid = invoke.Signature.ReturnSignatureType.StackKind == CliValueKind.Void;
        bool supported = parameters.Length <= MaximumArity &&
            parameters.All(IsRepresentableGenericArgument) &&
            (returnsVoid || IsRepresentableGenericArgument(invoke.Signature.ReturnSignatureType));
        if (!supported)
        {
            return new(
                factory,
                delegateType,
                invoke,
                ResolveThrowAdapter(factory, "ThrowUnsupported"),
                IsSupported: false);
        }

        // InvokeN<T0, ..., TResult>, or InvokeVoidN<T0, ...> for a void result, on ObjectArrayDelegateTarget.
        var adapter = ResolveAdapter(factory, parameters.Length, returnsVoid);
        var target = _methodInstances.ResolveMethodInstance(
            adapter.Key.Assembly,
            adapter.Key.MetadataToken,
            _symbols.Format(adapter),
            0,
            new CliGenericContext(
                [],
                returnsVoid ? parameters : [.. parameters, invoke.Signature.ReturnSignatureType]));
        return new(factory, delegateType, invoke, target, IsSupported: true);
    }

    // Func and Action stop at 16 parameters; so do the adapters.
    internal const int MaximumArity = 16;

    private static bool IsRepresentableGenericArgument(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified)
        {
            type = type.ElementType!;
        }

        return !type.ContainsGenericParameters &&
            type.Shape is not (
                CliTypeShape.ManagedByReference or
                CliTypeShape.UnmanagedPointer or
                CliTypeShape.FunctionPointer) &&
            type.StackKind is not (
                CliValueKind.Void or
                CliValueKind.ManagedAddress or
                CliValueKind.Unknown);
    }

    private MethodInstanceModel ResolveInvoke(
        MethodInstanceModel factory,
        CliTypeIdentity delegateType)
    {
        var definition = _typeDefinitions.ResolveTypeIdentity(delegateType);
        var candidates = definition.Methods
            .Select(_methods.GetMethod)
            .Where(method => method.Name == "Invoke" && !method.IsStatic)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw RuntimeContract(
                factory,
                $"delegate type '{delegateType.CanonicalName}' must define exactly one " +
                "instance Invoke method");
        }
        return _methodInstances.ResolveMethodInstance(
            candidates[0].Key.Assembly,
            candidates[0].Key.MetadataToken,
            _symbols.Format(candidates[0]),
            0,
            new CliGenericContext(
                delegateType.Shape == CliTypeShape.GenericInstantiation
                    ? delegateType.TypeArguments
                    : [],
                []));
    }

    private MethodDefinitionModel ResolveAdapter(
        MethodInstanceModel factory,
        int arity,
        bool returnsVoid)
    {
        var name = returnsVoid ? $"InvokeVoid{arity}" : $"Invoke{arity}";
        var candidates = _typeFinder.FindType(
                "System.Runtime.CompilerServices.ObjectArrayDelegateTarget")
            .Methods
            .Select(_methods.GetMethod)
            .Where(method =>
                method.Name == name &&
                !method.IsStatic &&
                method.GenericArity == arity + (returnsVoid ? 0 : 1) &&
                method.Signature.ParameterSignatureTypes.Length == arity)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw RuntimeContract(
                factory,
                $"object-array delegate support must define exactly one '{name}' adapter");
        }
        return candidates[0];
    }

    private MethodInstanceModel ResolveThrowAdapter(
        MethodInstanceModel factory,
        string name)
    {
        var candidates = _typeFinder.FindType(
                "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter")
            .Methods
            .Select(_methods.GetMethod)
            .Where(candidate =>
                candidate.Name == name &&
                candidate.IsStatic &&
                candidate.Signature.ParameterSignatureTypes.IsEmpty)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw RuntimeContract(
                factory,
                $"object-array delegate support must define exactly one '{name}' adapter");
        }
        var method = candidates[0];
        return _methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            _symbols.Format(method),
            0);
    }

    private static CompilerException RuntimeContract(
        MethodInstanceModel factory,
        string message) => new(new CompilerDiagnostic(
        DiagnosticCode.RuntimeContract,
        message,
        factory.CanonicalName));
}
