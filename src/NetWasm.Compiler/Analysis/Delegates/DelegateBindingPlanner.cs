using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Analysis.Delegates;

internal sealed class DelegateBindingPlanner(
    ITypeRelationshipClassifier relationships,
    IManagedMethodIdentityFactory identities) : IDelegateBindingPlanner
{
    private readonly ITypeRelationshipClassifier _relationships = relationships ??
        throw new ArgumentNullException(nameof(relationships));
    private readonly IManagedMethodIdentityFactory _identities = identities ??
        throw new ArgumentNullException(nameof(identities));

    public ImmutableArray<ManagedDelegateBinding> Plan(
        IEnumerable<MethodInstanceModel> invokes,
        IEnumerable<MethodInstanceModel> targets)
    {
        ArgumentNullException.ThrowIfNull(invokes);
        ArgumentNullException.ThrowIfNull(targets);

        var orderedInvokes = invokes
            .DistinctBy(invoke => invoke.CanonicalName)
            .OrderBy(invoke => invoke.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        var orderedTargets = targets
            .DistinctBy(target => target.CanonicalName)
            .OrderBy(target => target.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        var bindings = ImmutableArray.CreateBuilder<ManagedDelegateBinding>();
        foreach (var invoke in orderedInvokes)
        {
            foreach (var target in orderedTargets)
            {
                var binding = Bind(invoke, target);
                if (binding is not null)
                {
                    bindings.Add(binding);
                }
            }
        }

        return bindings.ToImmutable();
    }

    private ManagedDelegateBinding? Bind(
        MethodInstanceModel invoke,
        MethodInstanceModel target)
    {
        var invokeParameters = invoke.Signature.ParameterSignatureTypes;
        var targetParameters = target.Signature.ParameterSignatureTypes;
        if (invokeParameters.Length != targetParameters.Length)
        {
            return null;
        }

        var parameterBindings =
            ImmutableArray.CreateBuilder<ManagedDelegateValueBinding>(
                invokeParameters.Length);
        for (var index = 0; index < invokeParameters.Length; index++)
        {
            var parameter = BindValue(invokeParameters[index], targetParameters[index]);
            if (parameter is null)
            {
                return null;
            }

            parameterBindings.Add(parameter);
        }

        var returnBinding = BindValue(
            target.Signature.ReturnSignatureType,
            invoke.Signature.ReturnSignatureType);
        return returnBinding is null
            ? null
            : new ManagedDelegateBinding(
                _identities.Create(invoke),
                _identities.Create(target),
                invoke,
                target,
                parameterBindings.ToImmutable(),
                returnBinding);
    }

    private ManagedDelegateValueBinding? BindValue(
        CliTypeIdentity source,
        CliTypeIdentity target)
    {
        if (source.Equals(target))
        {
            return new(source, target, ManagedDelegateAdaptation.Identity);
        }

        return source.StackKind == CliValueKind.ManagedReference &&
               target.StackKind == CliValueKind.ManagedReference &&
               _relationships.Classify(source, target).IsHierarchyAssignable
            ? new(source, target, ManagedDelegateAdaptation.ReferenceConversion)
            : null;
    }
}
