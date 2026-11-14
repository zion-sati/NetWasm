using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Analysis.Loops.Elementwise;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IElementwiseLoopVectorizer
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Roslyn's `for (...; i < end; i++) { a[i] = b[i + 1] * c + i; ... }` computes
// each element from elements at the counter plus a constant, the counter and
// values the loop does not change. Ahead of the loop's entry jump, when
// i < end, every array is non-null and every access lies within its array for i
// and end - 1, a Vector128 loop runs four (or, for long and double, two)
// iterations at a time while that many remain, and the loop then runs what is
// left. The body holds nothing but those element stores, copies of the counter
// into locals (`k = i`) and the increment; elements are int, uint, long, ulong,
// float or double, one type per loop, and the operations are lane-wise and exact:
// wrapping integer add, subtract, multiply, bitwise operations and shifts by a
// constant, and floating add, subtract, multiply, divide and negate, so no
// iteration can throw and every lane computes what its iteration would. Values
// the loop does not change, including pure Math and MathF functions of them, are
// computed once and broadcast. Iterations depend on one another only through an
// array written at one offset and read or written at another; the same array
// that way is not vectorized, and two arrays that way run the vector loop only
// when they are different objects. A statement `x = x op value` folds such a
// value into an integer local x with a wrapping addition or a bitwise
// operation, each associative and commutative: the vector loop folds lanes into
// a vector that starts at the operation's identity, x then takes the fold of its
// lanes, and the loop folds what is left. A fold reads only arrays the loop does
// not write. A copy of an element into a local, as foreach makes, is that
// element where the body reads it, and the vector loop leaves it the last one.
// A span's element, as its inlined indexer reads it, is Unsafe.Add of the span's
// reference at the counter plus a constant; the indexer's check that index,
// unsigned, lies below a length the loop does not change, around a block that
// only throws, holds for every iteration the vector loop runs when the counter
// plus the constant is not negative there and lies below the length at end - 1,
// which the entry check tests. A loop that reads a span stores nothing: a store
// could reach memory the span covers.
//
// Roslyn's `do { x += p | 1; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0)`
// walks a reference over n elements instead. Where the loop's only other entry
// falls into its head and its body only folds and copies the element p
// points to, a vector loop ahead of the head runs while n, unsigned, holds
// `lanes` elements, advancing p and n alike; it leaves past the loop when n
// reaches zero, and otherwise the loop runs what is left. A loop entered with
// n below `lanes`, zero included, runs as before.
internal sealed class ElementwiseLoopVectorizer(
    IElementwiseLoopMatcher loops,
    IElementwiseWalkMatcher walks,
    IElementwiseLoopPrefixEmitter loopPrefixes,
    IElementwiseWalkPrefixEmitter walkPrefixes,
    ICalledMethodResolver calledMethods,
    bool enabled) : IElementwiseLoopVectorizer
{
    private readonly IElementwiseLoopMatcher _loops = loops ?? throw new ArgumentNullException(nameof(loops));
    private readonly IElementwiseWalkMatcher _walks = walks ?? throw new ArgumentNullException(nameof(walks));
    private readonly IElementwiseLoopPrefixEmitter _loopPrefixes = loopPrefixes ??
        throw new ArgumentNullException(nameof(loopPrefixes));
    private readonly IElementwiseWalkPrefixEmitter _walkPrefixes = walkPrefixes ??
        throw new ArgumentNullException(nameof(walkPrefixes));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction =>
                instruction.Operation is CilOperation.StoreArrayElement or CilOperation.LoadArrayElement or CilOperation.LoadObject))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var branches = instructions
            .Select(static instruction => instruction.Operand)
            .OfType<CilOperand.BranchTarget>()
            .GroupBy(static target => target.Offset)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        List<CilInstruction>[]? replacements = null;
        var depth = 0;
        for (var jump = 0; jump < instructions.Length; jump++)
        {
            if (instructions[jump] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var test } } ||
                branches[test] != 1 ||
                !indices.TryGetValue(test, out var condition) || condition <= jump + 4 ||
                _loops.Match(instructions, jump, condition, body.Locals) is not { } loop)
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            var prefix = _loopPrefixes.Emit(test, loop, locals, localTypes, out var loopDepth);
            replacements[jump] = [.. prefix, instructions[jump]];
            depth = Math.Max(depth, loopDepth);
        }
        foreach (var counted in CountedWalks.Find(body, _calledMethods))
        {
            // The instruction ahead of a walk falls into it, so it is no element
            // loop's entry jump, which the prefixes above replace.
            var start = counted.Head;
            if (_walks.Match(counted, body.Locals) is not { } walk)
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[start - 1] = _walkPrefixes.Emit(
                instructions[start - 1], walk, instructions[counted.Back + 1].Offset, locals, localTypes, out var loopDepth);
            depth = Math.Max(depth, loopDepth);
        }
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                MaxStack = body.MaxStack + depth,
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
    }
}
