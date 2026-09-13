using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Analysis.Delegates;

internal sealed class DiagnosticDelegateBindingPlanner(
    IDelegateBindingPlanner inner,
    ILogger<DiagnosticDelegateBindingPlanner> logger) : IDelegateBindingPlanner
{
    private static readonly Action<ILogger, int, Exception?> LogBindingCount =
        LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(4300, nameof(LogBindingCount)),
            "Planned {BindingCount} managed delegate bindings.");

    private readonly IDelegateBindingPlanner _inner = inner ??
        throw new ArgumentNullException(nameof(inner));
    private readonly ILogger<DiagnosticDelegateBindingPlanner> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));

    public ImmutableArray<ManagedDelegateBinding> Plan(
        IEnumerable<MethodInstanceModel> invokes,
        IEnumerable<MethodInstanceModel> targets)
    {
        var bindings = _inner.Plan(invokes, targets);
        LogBindingCount(_logger, bindings.Length, null);
        return bindings;
    }
}
