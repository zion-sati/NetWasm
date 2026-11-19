// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.DynamicCasts;

// IDynamicInterfaceCastable, as .NET's runtime honors it, for a program that allocates a class implementing it: each
// reachable body's interface casts, reference array stores and interface calls ask such an object (see
// DynamicCastBodyRewriter). A program without one is not rewritten, so it pays nothing. A body is rewritten once, all
// its sites together; the helpers' own bodies, which do the casts and stores the rewrite stands for, are left alone.
// Correctness, not an optimization: every preset runs it.
internal sealed class DynamicInterfaceCastableProgramReducer(
    ITypeRelationshipClassifier relationships,
    ICalledMethodResolver calls,
    ISymbolFormatter symbols,
    IDynamicCastBodyRewriter rewriter,
    CliTypeIdentity castable) : IReachableProgramReducer
{
    private readonly ITypeRelationshipClassifier _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
    private readonly IDynamicCastBodyRewriter _rewriter = rewriter ?? throw new ArgumentNullException(nameof(rewriter));
    private readonly CliTypeIdentity _castable = castable ?? throw new ArgumentNullException(nameof(castable));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        if (!program.AllocatedTypes.Any(type => !type.IsValueType && _relationships.Classify(type, _castable).IsAssignmentCompatible))
        {
            return result.ToImmutable();
        }
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values)
                     .DistinctBy(body => body.Method.CanonicalName))
        {
            if (IsHelper(method.Method.Definition.DeclaringType) || Rewritten(method.Body)) continue;
            var rewritten = _rewriter.Rewrite(method);
            if (!ReferenceEquals(rewritten, method.Body)) result.Add(method.Method.CanonicalName, rewritten);
        }
        return result.ToImmutable();
    }

    private bool IsHelper(EntityKey type) => _symbols.Format(type) == DynamicCastHelperResolver.HelpersType;

    private bool Rewritten(CilMethodBody body) => body.Instructions.Any(instruction =>
        instruction.Operation == CilOperation.Call && _calls.Resolve(instruction) is { } called &&
        IsHelper(called.Definition.DeclaringType));
}
