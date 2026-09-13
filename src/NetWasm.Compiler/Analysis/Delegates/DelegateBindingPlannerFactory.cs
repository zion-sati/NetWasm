using System;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Analysis.Delegates;

internal sealed class DelegateBindingPlannerFactory(
    IManagedMethodIdentityFactory identities,
    ILogger<DiagnosticDelegateBindingPlanner> logger) : IDelegateBindingPlannerFactory
{
    private readonly IManagedMethodIdentityFactory _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly ILogger<DiagnosticDelegateBindingPlanner> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));

    public IDelegateBindingPlanner Create(ITypeRelationshipClassifier relationships) =>
        new DiagnosticDelegateBindingPlanner(
            new DelegateBindingPlanner(relationships, _identities),
            _logger);
}
