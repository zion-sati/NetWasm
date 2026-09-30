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
        bool supported = invoke.Signature.ParameterSignatureTypes.Length == 1 &&
            invoke.Signature.ParameterSignatureTypes[0].StackKind !=
                CliValueKind.ManagedAddress &&
            invoke.Signature.ReturnType is not (
                CliValueKind.Void or CliValueKind.ManagedAddress);
        if (!supported)
        {
            return new(
                factory,
                delegateType,
                invoke,
                ResolveThrowAdapter(factory, "ThrowUnsupported"),
                IsSupported: false);
        }

        var unaryAdapter = ResolveUnaryAdapter(factory);
        var target = _methodInstances.ResolveMethodInstance(
            unaryAdapter.Key.Assembly,
            unaryAdapter.Key.MetadataToken,
            _symbols.Format(unaryAdapter),
            0,
            new CliGenericContext(
                [],
                [
                    invoke.Signature.ParameterSignatureTypes[0],
                    invoke.Signature.ReturnSignatureType,
                ]));
        return new(factory, delegateType, invoke, target, IsSupported: true);
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

    private MethodDefinitionModel ResolveUnaryAdapter(
        MethodInstanceModel factory)
    {
        var candidates = _typeFinder.FindType(
                "System.Runtime.CompilerServices.ObjectArrayDelegateTarget")
            .Methods
            .Select(_methods.GetMethod)
            .Where(method =>
                method.Name == "Invoke1" &&
                !method.IsStatic &&
                method.GenericArity == 2 &&
                method.Signature.ParameterSignatureTypes.Length == 1)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw RuntimeContract(
                factory,
                "object-array delegate support must define exactly one unary adapter");
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
