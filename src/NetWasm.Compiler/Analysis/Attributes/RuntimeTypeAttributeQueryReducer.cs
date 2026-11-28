// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

// Mediator: an attribute query left in the settled program is answered there. One whose target inlining or a constant
// argument named is answered at compile time, as the lowering answers one its body names. One whose target is a Type
// known only at run time (t.GetCustomAttributes(typeof(X), inherit),
// Attribute.IsDefined(t, typeof(X)), t.GetCustomAttribute<X>()) becomes a switch over the run-time types of the
// reachable program that carry X, each answering as the compile-time lowering would; any other type answers as a type
// without X does. The run-time types are the closure's (a typeof, a GetType() result, an allocation), so the switch roots
// no type, and only the queried attribute type is constructed, only for the types that carry it.
//
// The closure grows as the switch's attribute constructors reach new code, so each round lowers a query again from the
// method's own body; a body whose cases are unchanged is left as it is, which ends the reduction.
internal sealed class RuntimeTypeAttributeQueryReducer(
    ICalledMethodResolver calls,
    IAttributeQueryClassifier classifier,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IAttributeProvenanceAnalyzer provenance,
    IRuntimeTypeAttributeQueryAcceptor acceptor,
    IAttributeQueryCompileTimeAnswerer answers,
    IRuntimeTypeAttributeDispatchBuilder dispatch,
    IAttributeQueryBodyRewriter bodies,
    MethodInstanceModel ambiguityConstructor,
    MethodInstanceModel nullReferenceConstructor,
    MethodInstanceModel argumentNullConstructor) : IReachableProgramReducer
{
    // What each round lowered, by method, for the next round. An entry holds only while the program carries its
    // rewrite: a later closure build (a second analysis of the same program) starts from the bodies as read, and its
    // rounds must lower them again rather than take the earlier build's rewrite as done.
    private readonly Dictionary<string, LoweredBody> _lowered = new(StringComparer.Ordinal);

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var runtimeTypes = program.RuntimeTypes.Concat(program.AllocatedTypes).ToImmutableHashSet();
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .DistinctBy(body => body.Method.CanonicalName))
        {
            var name = method.Method.CanonicalName;
            var previous = _lowered.GetValueOrDefault(name) is { } lowered && Carries(method.Body, lowered) ? lowered : null;
            var source = previous?.Source ?? method.Body;
            if (Lower(source, runtimeTypes) is not { } next ||
                previous is not null && previous.Cases.SequenceEqual(next.Cases))
            {
                continue;
            }
            _lowered[name] = next;
            result.Add(name, next.Body);
        }
        return result.ToImmutable();
    }

    private LoweredBody? Lower(CilMethodBody body, ImmutableHashSet<CliTypeIdentity> runtimeTypes)
    {
        var queries = new List<(CilInstruction Instruction, MethodInstanceModel Method, AttributeQueryCall Call)>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operation is CilOperation.Call or CilOperation.CallVirtual &&
                calls.Resolve(instruction) is { } method && classifier.Classify(method) is { } call)
            {
                queries.Add((instruction, method, call));
            }
        }
        if (queries.Count == 0)
        {
            return null;
        }
        var proofs = provenance.Analyze(stacks.Validate(graphs.Build(body)));
        var replacements = ImmutableDictionary.CreateBuilder<int, AttributeCilFragment>();
        var cases = ImmutableArray.CreateBuilder<string>();
        var answeredOffsets = ImmutableHashSet.CreateBuilder<int>();
        var nextLocal = body.Locals.Length;
        foreach (var (instruction, method, call) in queries)
        {
            if (!proofs.TryGetValue(instruction.Offset, out var stack))
            {
                continue;
            }
            AttributeCilFragment fragment;
            switch (acceptor.Decide(call, stack, runtimeTypes))
            {
                case RuntimeTypeAttributeQueryDecision.Answered { Query: var query, Cases: var found }:
                    fragment = dispatch.Build(new(call, ArgumentTypes(method), found, Otherwise(query),
                        query.InheritConstant, query.ResultArrayType, ambiguityConstructor,
                        method.Definition.IsStatic ? argumentNullConstructor : nullReferenceConstructor,
                        method.Definition.IsStatic ? "element" : null, nextLocal));
                    cases.Add($"{instruction.Offset}:{string.Join(",", found.Select(found => found.Type.CanonicalName))}");
                    break;
                // Inlining or a constant argument named the target of a query whose own body did not: it is answered at
                // compile time here, as the lowering answers one its body names, so every level answers it alike. An
                // attribute that cannot be built leaves it to its run-time path, as for a run-time Type.
                case RuntimeTypeAttributeQueryDecision.NotRunTimeType when
                    answers.Answer(call, stack, nextLocal) is AttributeQueryAnswer.Answered { Fragment: var answered }:
                    fragment = answered;
                    cases.Add($"{instruction.Offset}:compile time");
                    break;
                // A query this declines keeps its run-time path; the settled program's report says why (NW1019).
                default:
                    continue;
            }
            replacements.Add(instruction.Offset, fragment);
            answeredOffsets.Add(instruction.SourceOffset);
            nextLocal += fragment.LocalTypes.Length;
        }
        return replacements.Count == 0
            ? null
            : new(body, bodies.Rewrite(body, replacements.ToImmutable()), cases.ToImmutable(), answeredOffsets.ToImmutable());
    }

    // The call's arguments as its signature types them: the receiver first for an instance method.
    private static ImmutableArray<CliTypeIdentity> ArgumentTypes(MethodInstanceModel method) => method.Definition.IsStatic
        ? method.Signature.ParameterSignatureTypes
        : [method.DeclaringType, .. method.Signature.ParameterSignatureTypes];

    // What a run-time type without the attribute answers: false, null or an empty array.
    private static AttributeQueryEntry Otherwise(ResolvedAttributeQuery query)
    {
        AttributeQueryResult none = query.Call.Operation == AttributeQueryOperation.IsDefined
            ? new AttributeExistenceResult(false)
            : new AttributeRetrievalResult([]);
        return new(null, query.Filter, none, none);
    }

    // Whether the body carries the rewrite: none of the queries the rewrite answered is still a call in it.
    private bool Carries(CilMethodBody body, LoweredBody lowered) => !body.Instructions.Any(instruction =>
        instruction.Operation is CilOperation.Call or CilOperation.CallVirtual &&
        lowered.Answered.Contains(instruction.SourceOffset) &&
        calls.Resolve(instruction) is { } method && classifier.Classify(method) is not null);

    private sealed record LoweredBody(CilMethodBody Source, CilMethodBody Body, ImmutableArray<string> Cases,
        ImmutableHashSet<int> Answered);
}
