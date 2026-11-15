// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal sealed class StaticArrayPopulationReducer(
    IStaticFieldObservationCollector observations,
    IStaticArrayPopulationPlanner populations) : IReachableProgramReducer
{
    private readonly IStaticFieldObservationCollector _observations = observations ?? throw new ArgumentNullException(nameof(observations));
    private readonly IStaticArrayPopulationPlanner _populations = populations ?? throw new ArgumentNullException(nameof(populations));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var observed = _observations.Collect(program, roots);
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values).DistinctBy(body => body.Method.CanonicalName))
        {
            var removed = _populations.Plan(method, observed);
            if (removed.IsEmpty) continue;
            result.Add(method.Method.CanonicalName, method.Body with
            {
                Instructions = [.. method.Body.Instructions.Select(instruction => removed.Contains(instruction.Offset)
                    ? instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() }
                    : instruction)],
            });
        }
        return result.ToImmutable();
    }
}
