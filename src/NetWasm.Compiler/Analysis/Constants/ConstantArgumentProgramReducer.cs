// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Replaces a parameter every direct caller passes the same constant with that constant. Where the direct callers pass
// one of a few integer constants, or pass on a parameter of their own that holds one, a switch on the parameter keeps
// only those cases: ThrowHelper.GetArgumentName's switch over every ExceptionArgument then keeps the names a program's
// reachable throws pass, so a program carries only their strings, not every one CoreLib has. A rewrite folds branches
// and calls, which can leave the callee's own calls passing constants, so the reducer takes its rewritten bodies'
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

    // More values than this are not worth tracking: a switch over them keeps most of its cases.
    internal const int MaximumValues = 64;

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
            var open = new bool[bodies.Length];
            for (var position = 0; position < bodies.Length; position++)
            {
                open[position] = boundaryKeys.Contains(bodies[position].Method.Definition.Key) ||
                    boundaries.Contains(bodies[position].Method.CanonicalName);
            }
            var inputs = Inputs(bodies, calls, open);
            var changed = false;
            for (var position = 0; position < bodies.Length; position++)
            {
                var method = bodies[position];
                if (open[position] || !inputs.TryGetValue(method.Method.CanonicalName, out var known)) continue;
                if (Specialize(method, known) is not { } rewritten) continue;
                result[method.Method.CanonicalName] = rewritten;
                bodies[position] = _analyzer.Analyze(new ReachableMethodRequest(method.Method) { RewrittenBody = rewritten }).Body;
                calls[position] = _arguments.Collect(bodies[position]);
                changed = true;
            }
            if (!changed) return result.ToImmutable();
        }
    }

    // The values each target's direct callers pass for each parameter: a set of constants, or null where a caller
    // passes anything else or a parameter of an open method (one called other than directly). A caller that passes on
    // its own parameter passes what that parameter holds, so the sets grow until none does.
    private static Dictionary<string, HashSet<ScalarConstant>?[]> Inputs(
        ManagedMethodBody[] bodies, ImmutableArray<ConstantCallEvaluation>[] calls, bool[] open)
    {
        var inputs = new Dictionary<string, HashSet<ScalarConstant>?[]>(StringComparer.Ordinal);
        for (var position = 0; position < bodies.Length; position++)
        {
            foreach (var call in calls[position])
            {
                if (!inputs.TryGetValue(call.Method.CanonicalName, out var known))
                {
                    inputs.Add(call.Method.CanonicalName, known = new HashSet<ScalarConstant>?[call.Arguments.Length]);
                    for (var index = 0; index < known.Length; index++) known[index] = [];
                }
                if (known.Length != call.Arguments.Length) throw new InvalidOperationException("Incoming calls disagree on their target signature.");
                for (var index = 0; index < known.Length; index++)
                {
                    if (call.Arguments[index] is { } constant && known[index] is { } values) Add(known, index, values, constant);
                    else if (call.Arguments[index] is null && Forwarded(call, index) < 0) known[index] = null;
                }
            }
        }
        for (var grew = true; grew;)
        {
            grew = false;
            for (var position = 0; position < bodies.Length; position++)
            {
                var caller = open[position] ? null : inputs.GetValueOrDefault(bodies[position].Method.CanonicalName);
                foreach (var call in calls[position])
                {
                    var known = inputs[call.Method.CanonicalName];
                    for (var index = 0; index < known.Length; index++)
                    {
                        if (Forwarded(call, index) is var parameter && parameter < 0 || known[index] is not { } values) continue;
                        if (caller?[parameter] is not { } passed)
                        {
                            known[index] = null;
                            grew = true;
                            continue;
                        }
                        foreach (var constant in passed)
                        {
                            if (!values.Contains(constant))
                            {
                                grew = true;
                                if (!Add(known, index, values, constant)) break;
                            }
                        }
                    }
                }
            }
        }
        return inputs;

        static int Forwarded(ConstantCallEvaluation call, int index) =>
            call.ForwardedParameters.IsDefault ? -1 : call.ForwardedParameters[index];

        // Adds the constant, or gives up on the parameter past MaximumValues; false once given up.
        static bool Add(HashSet<ScalarConstant>?[] known, int index, HashSet<ScalarConstant> values, ScalarConstant constant)
        {
            values.Add(constant);
            if (values.Count <= MaximumValues) return true;
            known[index] = null;
            return false;
        }
    }

    // The method with its constant parameters' loads replaced, its switches on parameters pruned and the result folded,
    // or null when nothing changes.
    private CilMethodBody? Specialize(ManagedMethodBody method, HashSet<ScalarConstant>?[] inputs)
    {
        // An instance method's argument 0 is its receiver; the collected constants are its declared parameters.
        var receiver = method.Method.Definition.IsStatic ? 0 : 1;
        var instructions = method.Body.Instructions;
        var values = (HashSet<ScalarConstant>?[])inputs.Clone();
        foreach (var instruction in instructions)
        {
            if (instruction is { Operation: CilOperation.LoadArgumentAddress or CilOperation.StoreArgument, Operand: CilOperand.Index index } &&
                (uint)(index.Value - receiver) < (uint)values.Length) values[index.Value - receiver] = null;
        }
        // A parameter every caller passes the same constant is that constant.
        var known = values.Select(static set => set is { Count: 1 } ? set.Single() : null).ToArray();
        var pruned = PrunedSwitches(instructions, receiver, values);

        ImmutableArray<CilInstruction>.Builder? replaced = null;
        for (var position = 0; position < instructions.Length; position++)
        {
            var instruction = instructions[position];
            var replacement = Replacement(method, instruction, receiver, known) ?? pruned.GetValueOrDefault(position);
            if (replacement is null)
            {
                replaced?.Add(instruction);
                continue;
            }
            if (replaced is null)
            {
                replaced = ImmutableArray.CreateBuilder<CilInstruction>(instructions.Length);
                replaced.AddRange(instructions, position);
            }
            replaced.Add(replacement);
        }
        return replaced is null ? null : _rewrites.Rewrite(method.Body with { Instructions = replaced.MoveToImmutable() });
    }

    // The reader lowers a switch to a test per case, each `dup; ldc.i4 i; bne.un next; pop; br case`, after the load of
    // the value it switches on. On a parameter whose callers pass only some of the cases' values, the test for any other
    // value never succeeds: its ldc.i4 becomes a pop and its bne.un a br to the next test, which leaves the stack as it
    // was, and the case only that test reached becomes unreachable. A test that anything but the one before it enters
    // may see another value, so the chain stops there.
    private static Dictionary<int, CilInstruction> PrunedSwitches(
        ImmutableArray<CilInstruction> instructions, int receiver, HashSet<ScalarConstant>?[] values)
    {
        var pruned = new Dictionary<int, CilInstruction>();
        if (!values.Any(static set => set is { Count: > 1 })) return pruned;
        var entries = new Dictionary<int, int>();
        foreach (var instruction in instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget { Offset: var target })
                entries[target] = entries.GetValueOrDefault(target) + 1;
        }
        for (var position = 0; position < instructions.Length; position++)
        {
            if (instructions[position] is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index index } ||
                (uint)(index.Value - receiver) >= (uint)values.Length ||
                values[index.Value - receiver] is not { Count: > 1 } set || !set.All(static value => value is ScalarConstant.Int32))
            {
                continue;
            }
            for (var test = position + 1; IsTest(instructions, test, entries, first: test == position + 1); test += 5)
            {
                if (set.Contains(new ScalarConstant.Int32(((CilOperand.ConstantI4)instructions[test + 1].Operand).Value))) continue;
                pruned[test + 1] = instructions[test + 1] with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };
                pruned[test + 2] = instructions[test + 2] with { Operation = CilOperation.Branch };
            }
        }
        return pruned;
    }

    // A lowered case test at index at, entered only by falling into it (the first) or from the test before.
    private static bool IsTest(ImmutableArray<CilInstruction> instructions, int at, Dictionary<int, int> entries, bool first) =>
        at + 4 < instructions.Length &&
        instructions[at].Operation == CilOperation.Duplicate &&
        entries.GetValueOrDefault(instructions[at].Offset) == (first ? 0 : 1) &&
        instructions[at + 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 } &&
        instructions[at + 2] is { Operation: CilOperation.BranchIfNotEqual, Operand: CilOperand.BranchTarget { Offset: var next } } &&
        next == instructions[at + 4].NextOffset &&
        instructions[at + 3].Operation == CilOperation.Pop &&
        instructions[at + 4].Operation == CilOperation.Branch;

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
