// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Replaces a parameter every direct caller passes the same constant with that constant. A rewrite folds branches and
// calls, which can leave the callee's own calls passing constants, so the reducer takes its rewritten bodies'
// analyses (through the closure's memo, which the next closure then reuses) and goes round again until no method
// changes: one reduction instead of a closure rebuild per call level. Callers the rewrites made unreachable still
// count until the closure drops them, so a later reduction may find more; it never finds fewer.
internal sealed class ConstantArgumentProgramReducer(
    ICallArgumentConstantCollector arguments,
    IMethodSpecializer rewrites,
    IReachableMethodAnalyzer analyzer) : IReachableProgramReducer
{
    private readonly ICallArgumentConstantCollector _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly IMethodSpecializer _rewrites = rewrites ?? throw new ArgumentNullException(nameof(rewrites));
    private readonly IReachableMethodAnalyzer _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var bodies = program.Methods.Values.Concat(program.ConstructedMethods.Values).DistinctBy(body => body.Method.CanonicalName).ToArray();
        var boundaryKeys = roots.Methods.Concat(roots.InvokedMethods).Concat(program.Exports.Select(export => export.Method)).ToHashSet();
        if (program.EntryPoint is not null) boundaryKeys.Add(program.EntryPoint.Key);
        var boundaries = program.CallableMethods.Keys.Concat(program.NativeCallbacks.Keys)
            .Concat(program.MethodDescriptors.Values.Select(method => method.CanonicalName))
            .Concat(program.DispatchCallSites.Values.SelectMany(site => site.Targets.Select(target => target.Method.CanonicalName)))
            .ToHashSet(StringComparer.Ordinal);
        var calls = new ImmutableArray<ConstantCallEvaluation>[bodies.Length];
        for (var position = 0; position < bodies.Length; position++) calls[position] = _arguments.Collect(bodies[position]);

        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        while (true)
        {
            var inputs = Inputs(calls);
            var changed = false;
            for (var position = 0; position < bodies.Length; position++)
            {
                var method = bodies[position];
                if (boundaryKeys.Contains(method.Method.Definition.Key) ||
                    boundaries.Contains(method.Method.CanonicalName) || !inputs.TryGetValue(method.Method.CanonicalName, out var known)) continue;
                if (Specialize(method, known) is not { } rewritten) continue;
                result[method.Method.CanonicalName] = rewritten;
                bodies[position] = _analyzer.Analyze(new ReachableMethodRequest(method.Method) { RewrittenBody = rewritten }).Body;
                calls[position] = _arguments.Collect(bodies[position]);
                changed = true;
            }
            if (!changed) return result.ToImmutable();
        }
    }

    // The constant each target's every caller passes for each parameter, or null.
    private static Dictionary<string, ScalarConstant?[]> Inputs(ImmutableArray<ConstantCallEvaluation>[] calls)
    {
        var inputs = new Dictionary<string, ScalarConstant?[]>(StringComparer.Ordinal);
        foreach (var bodyCalls in calls)
        {
            foreach (var call in bodyCalls)
            {
                if (!inputs.TryGetValue(call.Method.CanonicalName, out var known))
                {
                    inputs.Add(call.Method.CanonicalName, call.Arguments.ToArray());
                    continue;
                }
                if (known.Length != call.Arguments.Length) throw new InvalidOperationException("Incoming calls disagree on their target signature.");
                for (var index = 0; index < known.Length; index++)
                {
                    if (known[index] != call.Arguments[index]) known[index] = null;
                }
            }
        }
        return inputs;
    }

    // The method with its constant parameters' loads replaced and the result folded, or null when no load changes.
    private CilMethodBody? Specialize(ManagedMethodBody method, ScalarConstant?[] known)
    {
        // An instance method's argument 0 is its receiver; the collected constants are its declared parameters.
        var receiver = method.Method.Definition.IsStatic ? 0 : 1;
        var instructions = method.Body.Instructions;
        foreach (var instruction in instructions)
        {
            if (instruction is { Operation: CilOperation.LoadArgumentAddress or CilOperation.StoreArgument, Operand: CilOperand.Index index } &&
                (uint)(index.Value - receiver) < (uint)known.Length) known[index.Value - receiver] = null;
        }

        ImmutableArray<CilInstruction>.Builder? replaced = null;
        for (var position = 0; position < instructions.Length; position++)
        {
            var instruction = instructions[position];
            if (Replacement(method, instruction, receiver, known) is not { } replacement) continue;
            if (replaced is null)
            {
                replaced = ImmutableArray.CreateBuilder<CilInstruction>(instructions.Length);
                replaced.AddRange(instructions, position);
            }
            replaced.Add(replacement);
            for (position++; position < instructions.Length; position++)
            {
                replaced.Add(Replacement(method, instructions[position], receiver, known) ?? instructions[position]);
            }
        }
        return replaced is null ? null : _rewrites.Rewrite(method.Body with { Instructions = replaced.MoveToImmutable() });
    }

    private static CilInstruction? Replacement(ManagedMethodBody method, CilInstruction instruction, int receiver, ScalarConstant?[] known)
    {
        if (instruction is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index index } ||
            (uint)(index.Value - receiver) >= (uint)known.Length || known[index.Value - receiver] is not { } constant) return null;
        var kind = method.Method.Signature.ParameterTypes[index.Value - receiver];
        if (constant is ScalarConstant.Int32 integer && kind == CliValueKind.I4)
        {
            return instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(integer.Value) };
        }
        if (constant is ScalarConstant.Null && kind == CliValueKind.ManagedReference)
        {
            return instruction with { Operation = CilOperation.LoadNull, Operand = new CilOperand.None() };
        }
        return null;
    }
}
