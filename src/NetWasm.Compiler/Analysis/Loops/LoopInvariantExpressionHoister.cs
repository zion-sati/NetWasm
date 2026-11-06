using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface ILoopInvariantExpressionHoister
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A loop that computes, with branches, a value from values it never changes,
// such as an inlined helper that searches a vector it was given
// (dotnet/performance's SeekUnroll: `foundIndex = FindByte(ref vector)` a
// billion times), computes the value once ahead of its entry jump into a new
// local and reads the local instead. Cranelift already moves branch-free pure
// values out of loops, but not branches, so only an expression with a branch
// of its own moves.
//
// The loop is entered only through that jump, so the value is ready on every
// way in, and the expression runs on every iteration, outside the arms of the
// loop's branches and its handlers, so the loop never pays for one a rare path
// needs. The expression is a run of instructions that leaves one value on the
// stack it began with: constants, and scalar or vector arguments and locals
// the loop does not write and whose address the method never takes, arithmetic, comparisons and
// conversions that cannot throw (no integer division, no checked operations),
// pure calls (Math and MathF functions, vector reinterpretation, and a vector
// element read at a constant index inside the vector), forward branches that
// stay inside the run, and temporaries the run writes before it reads them on
// every path and nothing outside it touches. Such a run gives the same value on
// every iteration and cannot throw, so computing it once, even ahead of a loop
// that runs no iteration, gives every iteration's value.
internal sealed class LoopInvariantExpressionHoister(
    ICalledMethodResolver calledMethods,
    IRuntimeIntrinsicRegistry intrinsics,
    bool enabled) : ILoopInvariantExpressionHoister
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction => instruction.Operation == CilOperation.Branch))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var sources = BranchSources(instructions, indices);
        var targets = instructions
            .Select(instruction => instruction.Operand is CilOperand.BranchTarget { Offset: var offset } &&
                indices.TryGetValue(offset, out var target) ? target : -1)
            .ToArray();
        var handlers = body.ExceptionRegions
            .SelectMany(static region => (IEnumerable<(int Start, int End)>)(region.FilterOffset is { } filter
                ? [(filter, region.HandlerOffset), (region.HandlerOffset, region.HandlerOffset + region.HandlerLength)]
                : [(region.HandlerOffset, region.HandlerOffset + region.HandlerLength)]))
            .ToArray();
        var boundaries = body.ExceptionRegions
            .SelectMany(static region => (int?[])
            [
                region.TryOffset, region.TryOffset + region.TryLength,
                region.HandlerOffset, region.HandlerOffset + region.HandlerLength, region.FilterOffset,
            ])
            .OfType<int>()
            .ToHashSet();
        var exposed = ExposedSlots.Of(instructions);
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        List<CilInstruction>[]? replacements = null;
        var claimed = new HashSet<int>();
        var depth = 0;
        for (var jump = 0; jump < instructions.Length; jump++)
        {
            // A loop Roslyn enters through a jump to its test: the body runs from
            // the jump's successor to the backward branch at the test's end.
            if (instructions[jump] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var test } } ||
                !indices.TryGetValue(test, out var condition) || condition <= jump + 1)
            {
                continue;
            }
            var start = instructions[jump + 1].Offset;
            var end = -1;
            for (var index = condition; index < instructions.Length; index++)
            {
                if (instructions[index].Operand is CilOperand.BranchTarget { Offset: var back } && back == start)
                {
                    end = index;
                    break;
                }
                if (instructions[index].Operation is CilOperation.Return or CilOperation.Throw or CilOperation.Leave)
                {
                    break;
                }
            }
            if (end < 0 || EnteredElsewhere(sources, jump, end)) continue;
            var written = Written(instructions, jump + 1, end);
            written.UnionWith(exposed);
            var rare = Conditional(instructions, indices, targets, handlers, jump, end);

            List<CilInstruction>? prefix = null;
            for (var first = jump + 1; first < end;)
            {
                if (claimed.Contains(first) || rare[first - jump - 1] ||
                    Expression(body, targets, first, end, written, exposed, sources, boundaries) is not { } expression)
                {
                    first++;
                    continue;
                }
                var (last, kind, height) = expression;
                depth = Math.Max(depth, height);
                replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
                prefix ??= [];
                var type = kind switch
                {
                    CliValueKind.I4 => CliTypeIdentity.Primitive("i4", kind),
                    CliValueKind.I8 => CliTypeIdentity.Primitive("i8", kind),
                    CliValueKind.F4 => CliTypeIdentity.Primitive("f4", kind),
                    _ => CliTypeIdentity.Primitive("f8", kind),
                };
                locals.Add(kind);
                localTypes.Add(type);
                var local = locals.Count - 1;
                // The copy's branches land on its own instructions, and a branch
                // past its end on the store that keeps the value.
                var copyStart = prefix.Count;
                for (var index = first; index < last; index++)
                {
                    var instruction = instructions[index];
                    var operand = instruction.Operand is CilOperand.BranchTarget
                        ? RenumberedInstructions.Local(copyStart + targets[index] - first)
                        : instruction.Operand;
                    prefix.Add(new CilInstruction(0, 1, instruction.Operation, operand));
                    replacements[index] = [];
                    claimed.Add(index);
                }
                prefix.Add(new CilInstruction(0, 1, CilOperation.StoreLocal, new CilOperand.Index(local)));
                // The first instruction keeps its offset, so a jump to it still lands.
                replacements[first] = [new CilInstruction(0, 1, CilOperation.LoadLocal, new CilOperand.Index(local))];
                first = last;
            }
            if (prefix is not null)
            {
                replacements![jump] = [.. prefix, instructions[jump]];
            }
        }
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                // The copies run on whatever the entry jump has on the stack.
                MaxStack = body.MaxStack + depth,
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
    }

    // The end (exclusive), value kind and stack height of the longest expression
    // that starts at first and ends before limit, or null when none with a branch does.
    private (int Last, CliValueKind Kind, int Height)? Expression(
        CilMethodBody body,
        int[] targets,
        int first,
        int limit,
        HashSet<(bool Argument, int Index)> written,
        HashSet<(bool Argument, int Index)> exposed,
        Dictionary<int, List<int>> sources,
        HashSet<int> boundaries)
    {
        var instructions = body.Instructions;
        var state = new State([], []);
        var reachable = true;
        var pending = new Dictionary<int, State>();
        var branches = 0;
        var height = 0;
        var candidates = new List<(int Last, CliValueKind Kind, int Height)>();
        for (var index = first; index < limit; index++)
        {
            if (pending.Remove(index, out var arriving))
            {
                if (reachable && !state.Matches(arriving)) break;
                state = reachable ? state.Meet(arriving) : arriving;
                reachable = true;
            }
            // Code no path reaches, a jump from outside, or a protected region's
            // edge ends the expression.
            if (!reachable ||
                index > first && sources.TryGetValue(index, out var from) && from.Any(source => source < first || source >= index) ||
                index > first && boundaries.Contains(instructions[index].Offset))
            {
                break;
            }
            if (!Step(body, targets, first, index, state, pending, written, exposed, ref reachable, ref branches))
            {
                break;
            }
            // No instruction here holds more values within its step than before or after it.
            height = Math.Max(height, state.Stack.Count);
            // The run so far is an expression when every path through it leaves
            // one scalar value and every branch in it has landed.
            var next = index + 1;
            if (branches > 0 && pending.Keys.All(target => target == next) &&
                End(state, reachable, pending.TryGetValue(next, out var atEnd) ? atEnd : null) is { } kind)
            {
                candidates.Add((next, kind, height));
            }
        }
        // The longest run whose temporaries nothing else touches.
        for (var candidate = candidates.Count - 1; candidate >= 0; candidate--)
        {
            if (Temporaries(instructions, first, candidates[candidate].Last, exposed)) return candidates[candidate];
        }
        return null;
    }

    // The value kind at the expression's end, from the fall-through path and a
    // branch to the end; null unless each leaves the same single scalar.
    private static CliValueKind? End(State state, bool reachable, State? arriving)
    {
        var stack = reachable ? state.Stack : arriving?.Stack;
        if (stack is not [var kind] || arriving is not null && reachable && !state.Matches(arriving))
        {
            return null;
        }
        return kind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 ? kind : null;
    }

    // Applies one instruction to the stack and the temporaries written on the
    // way to it; false for an instruction an expression cannot contain.
    private bool Step(
        CilMethodBody body,
        int[] targets,
        int first,
        int index,
        State state,
        Dictionary<int, State> pending,
        HashSet<(bool Argument, int Index)> written,
        HashSet<(bool Argument, int Index)> exposed,
        ref bool reachable,
        ref int branches)
    {
        var instructions = body.Instructions;
        var instruction = instructions[index];
        var stack = state.Stack;
        switch (instruction.Operation)
        {
            case CilOperation.Nop:
                return true;
            case CilOperation.LoadInt32:
                stack.Add(CliValueKind.I4);
                return true;
            case CilOperation.LoadInt64:
                stack.Add(CliValueKind.I8);
                return true;
            case CilOperation.LoadFloat32:
                stack.Add(CliValueKind.F4);
                return true;
            case CilOperation.LoadFloat64:
                stack.Add(CliValueKind.F8);
                return true;
            case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local } &&
                (uint)local < (uint)body.Locals.Length && Scalar(body.Locals[local]):
                // A local the loop changes is read only as a temporary this run wrote.
                if (written.Contains((false, local)) && !state.Assigned.Contains(local)) return false;
                stack.Add(body.Locals[local]);
                return true;
            case CilOperation.LoadArgument when instruction.Operand is CilOperand.Index { Value: var argument }:
                if (written.Contains((true, argument)) || Argument(body, argument) is not { } kind) return false;
                stack.Add(kind);
                return true;
            case CilOperation.StoreLocal when instruction.Operand is CilOperand.Index { Value: var local } &&
                (uint)local < (uint)body.Locals.Length && !exposed.Contains((false, local)):
                if (stack.Count == 0) return false;
                stack.RemoveAt(stack.Count - 1);
                state.Assigned.Add(local);
                return true;
            case CilOperation.Duplicate:
                if (stack.Count == 0) return false;
                stack.Add(stack[^1]);
                return true;
            case CilOperation.Pop:
                return Pop(stack, 1);
            case CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or
                CilOperation.BitwiseAnd or CilOperation.BitwiseOr or CilOperation.BitwiseXor:
                return Binary(stack, integerAllowed: true);
            case CilOperation.Divide or CilOperation.Remainder:
                // Only a floating division or remainder, which cannot throw.
                return Binary(stack, integerAllowed: false);
            case CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned:
                if (!Pop(stack, 1)) return false;
                return stack.Count > 0;
            case CilOperation.Negate or CilOperation.OnesComplement:
                return stack.Count > 0;
            case CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or CilOperation.CompareGreaterThanUnsigned or
                CilOperation.CompareLessThanSigned or CilOperation.CompareLessThanUnsigned:
                if (!Pop(stack, 2)) return false;
                stack.Add(CliValueKind.I4);
                return true;
            case CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned:
                return Convert(stack, CliValueKind.I4);
            case CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned:
                return Convert(stack, CliValueKind.I8);
            case CilOperation.ConvertFloat32:
                return Convert(stack, CliValueKind.F4);
            case CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned:
                return Convert(stack, CliValueKind.F8);
            case CilOperation.Branch:
                if (targets[index] <= index || !Arrive(pending, targets[index], state.Copy())) return false;
                reachable = false;
                branches++;
                return true;
            case CilOperation.BranchIfTrue or CilOperation.BranchIfFalse:
                return Pop(stack, 1) && Conditional(targets, index, state, pending, ref branches);
            case CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned:
                return Pop(stack, 2) && Conditional(targets, index, state, pending, ref branches);
            case CilOperation.Call when _calledMethods.Resolve(instruction) is { } method && Pure(instructions, first, index, method):
                var arguments = method.Signature.ParameterTypes.Length + (method.Definition.IsStatic ? 0 : 1);
                if (!Pop(stack, arguments)) return false;
                if (method.Signature.ReturnType != CliValueKind.Void) stack.Add(method.Signature.ReturnType);
                return true;
            default:
                return false;
        }
    }

    // A branch inside the run lands ahead of it.
    private static bool Conditional(int[] targets, int index, State state, Dictionary<int, State> pending, ref int branches)
    {
        if (targets[index] <= index || !Arrive(pending, targets[index], state.Copy())) return false;
        branches++;
        return true;
    }

    private static bool Arrive(Dictionary<int, State> pending, int target, State state)
    {
        if (!pending.TryGetValue(target, out var earlier))
        {
            pending.Add(target, state);
            return true;
        }
        if (!earlier.Matches(state)) return false;
        pending[target] = earlier.Meet(state);
        return true;
    }

    // A pure call that cannot throw: a Math or MathF function, a vector
    // reinterpretation, or a vector element read at a constant index inside the
    // vector, which the emitter folds to that lane's extract.
    private bool Pure(ImmutableArray<CilInstruction> instructions, int first, int index, MethodInstanceModel method)
    {
        if (LoopInvariantCallHoister.IsPure(method)) return true;
        if (!_intrinsics.TryGetIntrinsic(method.Definition.Key, out var intrinsic)) return false;
        return intrinsic switch
        {
            RuntimeIntrinsic.SimdReinterpret => true,
            RuntimeIntrinsic.SimdGetElement =>
                index > first &&
                instructions[index - 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var lane } } &&
                Lanes(method) is { } lanes && (uint)lane < (uint)lanes,
            _ => false,
        };
    }

    // The lane count of the vector a GetElement reads: a 16-byte vector of its
    // element type.
    private static int? Lanes(MethodInstanceModel method)
    {
        var element = method.Definition.IsStatic
            ? method.MethodArguments.FirstOrDefault()
            : method.DeclaringType.TypeArguments.FirstOrDefault();
        return element?.CanonicalName switch
        {
            "primitive:i1" or "primitive:u1" => 16,
            "primitive:i2" or "primitive:u2" or "primitive:char" => 8,
            "primitive:i4" or "primitive:u4" or "primitive:f4" => 4,
            "primitive:i8" or "primitive:u8" or "primitive:f8" => 2,
            _ => null,
        };
    }

    // Every temporary the run writes is written before it is read on each path
    // (checked as the run is read) and touched nowhere else in the method.
    private static bool Temporaries(
        ImmutableArray<CilInstruction> instructions,
        int first,
        int last,
        HashSet<(bool Argument, int Index)> exposed)
    {
        var temporaries = new HashSet<int>();
        for (var index = first; index < last; index++)
        {
            if (instructions[index] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } })
            {
                temporaries.Add(local);
            }
        }
        if (temporaries.Count == 0) return true;
        for (var index = 0; index < instructions.Length; index++)
        {
            if ((index < first || index >= last) &&
                instructions[index] is
                {
                    Operation: CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index { Value: var local },
                } &&
                temporaries.Contains(local))
            {
                return false;
            }
        }
        return !temporaries.Any(local => exposed.Contains((false, local)));
    }

    // Whether each instruction of the loop, from the entry jump's successor to
    // the backward branch, can be skipped on an iteration that goes on: one
    // inside the arm of a forward branch that lands in the loop, or in a handler.
    // A branch out of the loop ends the iteration, so code after it still runs on
    // every iteration but the last.
    private static bool[] Conditional(
        ImmutableArray<CilInstruction> instructions,
        Dictionary<int, int> indices,
        int[] targets,
        (int Start, int End)[] handlers,
        int jump,
        int end)
    {
        // Each forward branch adds one at its successor and takes it back at its landing.
        var arms = new int[end - jump + 1];
        for (var source = jump + 1; source <= end; source++)
        {
            IEnumerable<int> landings = instructions[source].Operand switch
            {
                CilOperand.SwitchTargets { Offsets: var offsets } =>
                    offsets.Select(offset => indices.TryGetValue(offset, out var target) ? target : -1),
                _ => [targets[source]],
            };
            foreach (var target in landings)
            {
                if (target <= source || target > end) continue;
                arms[source + 1 - jump - 1]++;
                arms[target - jump - 1]--;
            }
        }
        var rare = new bool[end - jump];
        var depth = 0;
        for (var index = jump + 1; index <= end; index++)
        {
            depth += arms[index - jump - 1];
            var offset = instructions[index].Offset;
            rare[index - jump - 1] = depth > 0 || handlers.Any(handler => offset >= handler.Start && offset < handler.End);
        }
        return rare;
    }

    // A branch from outside the loop lands in its body or test.
    private static bool EnteredElsewhere(Dictionary<int, List<int>> sources, int jump, int end)
    {
        for (var index = jump + 1; index <= end; index++)
        {
            if (sources.TryGetValue(index, out var from) && from.Any(source => source < jump || source > end)) return true;
        }
        return false;
    }

    // The locals and arguments the loop writes or takes the address of.
    private static HashSet<(bool Argument, int Index)> Written(ImmutableArray<CilInstruction> instructions, int start, int end)
    {
        var written = new HashSet<(bool, int)>();
        for (var index = start; index <= end; index++)
        {
            switch (instructions[index])
            {
                case { Operation: CilOperation.StoreLocal or CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }:
                    written.Add((false, local));
                    break;
                case { Operation: CilOperation.StoreArgument or CilOperation.LoadArgumentAddress, Operand: CilOperand.Index { Value: var argument } }:
                    written.Add((true, argument));
                    break;
            }
        }
        return written;
    }

    // Each instruction's index, by the indices of the branches and switches that land on it.
    private static Dictionary<int, List<int>> BranchSources(ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices)
    {
        var sources = new Dictionary<int, List<int>>();
        for (var index = 0; index < instructions.Length; index++)
        {
            IEnumerable<int> targets = instructions[index].Operand switch
            {
                CilOperand.BranchTarget { Offset: var offset } => [offset],
                CilOperand.SwitchTargets { Offsets: var offsets } => offsets,
                _ => [],
            };
            foreach (var offset in targets)
            {
                if (!indices.TryGetValue(offset, out var target)) continue;
                if (!sources.TryGetValue(target, out var list)) sources[target] = list = [];
                list.Add(index);
            }
        }
        return sources;
    }

    private static bool Pop(List<CliValueKind> stack, int count)
    {
        if (stack.Count < count) return false;
        stack.RemoveRange(stack.Count - count, count);
        return true;
    }

    private static bool Binary(List<CliValueKind> stack, bool integerAllowed)
    {
        if (stack.Count < 2) return false;
        var right = stack[^1];
        var left = stack[^2];
        if (!integerAllowed && (left is not (CliValueKind.F4 or CliValueKind.F8) || right is not (CliValueKind.F4 or CliValueKind.F8)))
        {
            return false;
        }
        stack.RemoveAt(stack.Count - 1);
        stack[^1] = left == right ? left : left is CliValueKind.F4 or CliValueKind.F8 ? left : right;
        return true;
    }

    private static bool Convert(List<CliValueKind> stack, CliValueKind kind)
    {
        if (stack.Count == 0) return false;
        stack[^1] = kind;
        return true;
    }

    private static CliValueKind? Argument(CilMethodBody body, int argument)
    {
        if (!body.Method.IsStatic)
        {
            if (argument == 0) return null;
            argument--;
        }
        var parameters = body.Method.Signature.ParameterTypes;
        return (uint)argument < (uint)parameters.Length && Scalar(parameters[argument]) ? parameters[argument] : null;
    }

    // A value read by value: a reference or address would read memory the loop may change.
    private static bool Scalar(CliValueKind kind) =>
        kind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.V128;

    // The value kinds on the stack above the expression's start, and the
    // temporaries written on every path to this point.
    private sealed record State(List<CliValueKind> Stack, HashSet<int> Assigned)
    {
        public State Copy() => new([.. Stack], [.. Assigned]);

        public bool Matches(State other) => Stack.SequenceEqual(other.Stack);

        // Where two paths meet, a temporary counts as written only when both wrote it.
        public State Meet(State other) => new([.. Stack], [.. Assigned.Intersect(other.Assigned)]);
    }
}
