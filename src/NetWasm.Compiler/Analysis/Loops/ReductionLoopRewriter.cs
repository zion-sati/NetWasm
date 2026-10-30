using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IReductionLoopRewriter
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Roslyn's `for (...; i < end; i++) { x ^= data[i + c]; y += other[i]; }` and
// `foreach (var e in data) m = Math.Max(m, e);` fold elements of arrays or spans
// into locals one at a time. Ahead of the loop's entry jump, when i < end and
// every access lies within its array or span for i and end - 1, an ArrayLoops
// method folds each statement's whole range at once, the statement applies the
// result as it applies one element, a local the body copies an element to gets
// the last one, and i becomes end, so the loop only tests. Otherwise the loop
// runs as before. The body holds nothing but those copies, the folds and the
// increment: each fold reads its local and one element at i plus a constant,
// or a copy of one, and applies a bitwise operation, a wrapping addition or an
// integer Math.Min or Math.Max, then writes the local back. Each of these is
// associative and commutative. Bitwise operations and addition commute with
// truncation, so a fold may truncate; minimums and maximums commute with the
// widening of their elements, and the bitwise operations with any widening.
// Additions sum their elements at the width they widen to. end is an expression
// of constants, arguments, lengths and locals the loop does not write. This
// runs before inlining, while a span's indexer is still a call.
internal sealed class ReductionLoopRewriter(
    ICalledMethodResolver calledMethods,
    IArrayLoopMethodResolver methods,
    ISpanLengthResolver spanLengths,
    bool enabled) : IReductionLoopRewriter
{
    private const int MaximumOffset = 1 << 16;
    private const int MaximumBoundLength = 8;

    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IArrayLoopMethodResolver _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly ISpanLengthResolver _spanLengths = spanLengths ?? throw new ArgumentNullException(nameof(spanLengths));

    // An element the body reads: an array or span at the counter plus Offset.
    private sealed record Access(CilInstruction Base, MethodInstanceModel? Indexer, CliTypeIdentity Element, int Offset);

    // x = x op element: Helper, instantiated for Lane, folds the range,
    // Widening follows its result, and Combine is the operation or the Math.Min
    // or Max call.
    private sealed record Fold(int Local, Access Access, string Helper, CliTypeIdentity Lane,
        ImmutableArray<CilInstruction> Widening, CilInstruction Combine, CilInstruction? Truncation);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction =>
                instruction.Operation is CilOperation.BitwiseXor or CilOperation.BitwiseOr or CilOperation.BitwiseAnd or
                    CilOperation.Add or CilOperation.Call))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        // A test that something besides its entry jump branches to is not a
        // plain loop's, or it already has its guards from an earlier round.
        var branches = instructions
            .SelectMany(static instruction => instruction.Operand switch
            {
                CilOperand.BranchTarget target => [target.Offset],
                CilOperand.SwitchTargets targets => targets.Offsets,
                _ => ImmutableArray<int>.Empty,
            })
            .GroupBy(static offset => offset)
            .ToDictionary(static group => group.Key, static group => group.Count());
        List<CilInstruction>[]? replacements = null;
        for (var jump = 0; jump < instructions.Length; jump++)
        {
            if (instructions[jump] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var test } } ||
                branches[test] != 1 ||
                !indices.TryGetValue(test, out var condition) || condition <= jump + 4 ||
                Match(instructions, jump, condition) is not { } prefix)
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[jump] = [.. prefix, instructions[jump]];
        }
        // The prefix's guards and calls run above whatever the jump leaves, and
        // each call's emission uses two slots above its arguments.
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with { MaxStack = body.MaxStack + 8 };
    }

    private List<CilInstruction>? Match(ImmutableArray<CilInstruction> instructions, int jump, int condition)
    {
        if (instructions[condition] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var counter } })
        {
            return null;
        }
        var branch = -1;
        for (var index = condition + 1; index < instructions.Length && index <= condition + MaximumBoundLength + 1; index++)
        {
            if (instructions[index].Operation == CilOperation.BranchIfLessThanSigned)
            {
                branch = index;
                break;
            }
        }
        if (branch < 0 || instructions[branch].Operand is not CilOperand.BranchTarget { Offset: var start } ||
            start != instructions[jump + 1].Offset)
        {
            return null;
        }
        var increment = condition - 4;
        if (instructions[increment] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } ||
            read != counter ||
            instructions[increment + 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 1 } } ||
            instructions[increment + 2].Operation != CilOperation.Add ||
            instructions[increment + 3] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var written } } ||
            written != counter)
        {
            return null;
        }

        var folds = new List<Fold>();
        // The locals the body copies elements to, with the element each holds.
        var copies = new Dictionary<int, Access>();
        for (var position = jump + 1; position < increment;)
        {
            if (instructions[position].Operation == CilOperation.Nop)
            {
                position++;
                continue;
            }
            if (ParseAccess(instructions, position, increment, counter) is var (copied, after) && after < increment &&
                instructions[after] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var copy } } &&
                copy != counter)
            {
                copies[copy] = copied;
                position = after + 1;
                continue;
            }
            if (ParseFold(instructions, position, increment, counter, copies) is not var (fold, next)) return null;
            folds.Add(fold);
            position = next;
        }
        if (folds.Count == 0) return null;
        var locals = folds.Select(static fold => fold.Local).ToHashSet();
        var changed = locals.Concat(copies.Keys).ToHashSet();
        if (locals.Contains(counter) || locals.Overlaps(copies.Keys) ||
            folds.GroupBy(static fold => fold.Local).Any(static group =>
                group.Select(static fold => (fold.Combine.Operation, fold.Combine.Operand)).Distinct().Count() > 1) ||
            folds.Select(static fold => fold.Access).Concat(copies.Values).Any(access => Writes(access.Base, changed)))
        {
            return null;
        }
        var bound = instructions.AsSpan(condition + 1, branch - condition - 1).ToArray();
        if (!IsInvariantBound(bound, counter, changed)) return null;

        return Prefix(instructions[condition].Offset, counter, bound, folds, copies);
    }

    // One fold: ldloc x; value; [widening]; operation; [truncation]; stloc x, or
    // with the value and its widening first. The value is an element or a local
    // the body copied one to.
    private (Fold Fold, int Next)? ParseFold(ImmutableArray<CilInstruction> instructions, int position, int end, int counter,
        IReadOnlyDictionary<int, Access> copies)
    {
        var widening = ImmutableArray.CreateBuilder<CilInstruction>();
        int local;
        Access access;
        int next;
        if (instructions[position] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var first } } &&
            first != counter && !copies.ContainsKey(first) &&
            Value(instructions, position + 1, end, counter, copies) is var (value, afterValue))
        {
            (local, access, next) = (first, value, afterValue);
            while (next < end && IsIntegerConversion(instructions[next])) widening.Add(instructions[next++]);
        }
        else if (Value(instructions, position, end, counter, copies) is var (leading, afterLeading))
        {
            (access, next) = (leading, afterLeading);
            while (next < end && IsIntegerConversion(instructions[next])) widening.Add(instructions[next++]);
            if (next >= end ||
                instructions[next] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var second } } ||
                second == counter || copies.ContainsKey(second))
            {
                return null;
            }
            (local, next) = (second, next + 1);
        }
        else
        {
            return null;
        }
        if (next >= end) return null;
        var combine = instructions[next++];
        CilInstruction? truncation = null;
        if (next < end && IsIntegerConversion(instructions[next])) truncation = instructions[next++];
        if (next >= end ||
            instructions[next] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } ||
            stored != local ||
            Helper(access.Element, widening, combine, truncation) is not var (helper, lane, rewidening))
        {
            return null;
        }
        return (new Fold(local, access, helper, lane, rewidening, combine, truncation), next + 1);
    }

    // An element, or a local the body copied one to.
    private (Access Access, int Next)? Value(ImmutableArray<CilInstruction> instructions, int position, int end, int counter,
        IReadOnlyDictionary<int, Access> copies) =>
        position < end && instructions[position] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } &&
        copies.TryGetValue(local, out var copied)
            ? (copied, position + 1)
            : ParseAccess(instructions, position, end, counter);

    // The ArrayLoops method that folds a range for the operation, the element
    // type it is instantiated for, and the conversions that follow its result.
    private (string Helper, CliTypeIdentity Lane, ImmutableArray<CilInstruction> Widening)? Helper(
        CliTypeIdentity element,
        ImmutableArray<CilInstruction>.Builder widening,
        CilInstruction combine,
        CilInstruction? truncation)
    {
        var width = PrimitiveElements.Size(element) == 8 ? "64" : "32";
        var conversion = widening is [var single] ? single.Operation : (CilOperation?)null;
        var signed = element.CanonicalName is "primitive:i1" or "primitive:i2" or "primitive:i4" or "primitive:i8";
        var narrow = PrimitiveElements.Size(element) is 1 or 2;
        var integer = element.CanonicalName != "primitive:bool";
        // An element widened to 64 bits as its own sign says: conv.i8 of a signed
        // element or of a narrow unsigned one, which int holds as non-negative,
        // or conv.u8 of an unsigned one.
        var extended = PrimitiveElements.Size(element) != 8 &&
            (conversion == CilOperation.ConvertInt64 && (signed || narrow) ||
                conversion == CilOperation.ConvertInt64Unsigned && !signed);
        switch (combine.Operation)
        {
            case CilOperation.BitwiseXor or CilOperation.BitwiseOr or CilOperation.BitwiseAnd:
                return (combine.Operation switch
                {
                    CilOperation.BitwiseXor => "Xor",
                    CilOperation.BitwiseOr => "Or",
                    _ => "And",
                } + width, element, widening.ToImmutable());
            case CilOperation.Add when integer && (widening.Count == 0 || extended):
                return ("Sum" + (extended ? "64" : width), element, []);
            case CilOperation.Call when integer && truncation is null &&
                _calledMethods.Resolve(combine) is
                {
                    Definition: { Name: "Min" or "Max" } definition,
                    DeclaringType.FullName: "System.Math",
                    Signature.ParameterSignatureTypes: [var left, var right],
                } &&
                left.CanonicalName == right.CanonicalName &&
                Ordered(element, left.CanonicalName, widening.Count == 0 ? null : conversion, signed, narrow, extended):
                // IL loads an eight-byte element as signed whatever its sign, so
                // the comparison's type says how to order it.
                return (definition.Name + width, PrimitiveElements.Size(element) == 8 ? left : element, widening.ToImmutable());
            default:
                return null;
        }
    }

    // Whether Math.Min or Max over the comparison type orders the elements,
    // widened as the fold widens them, as their own type does. A truncating
    // fold could reorder them, so it is not one.
    private static bool Ordered(CliTypeIdentity element, string comparison, CilOperation? conversion, bool signed, bool narrow,
        bool extended) =>
        conversion switch
        {
            null => comparison == element.CanonicalName ||
                narrow && (comparison == "primitive:i4" || comparison == "primitive:u4" && !signed) ||
                PrimitiveElements.Size(element) == 8 && comparison is "primitive:i8" or "primitive:u8",
            CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned => extended &&
                (comparison == "primitive:i8" || comparison == "primitive:u8" && !signed),
            _ => false,
        };

    // array[index], with ldelem, or span[index], with the indexer's call and a load.
    private (Access Access, int Next)? ParseAccess(ImmutableArray<CilInstruction> instructions, int position, int end, int counter)
    {
        if (position >= end) return null;
        var source = instructions[position];
        var span = source.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress;
        if (!span && !IsLoad(source, counter) ||
            span && source is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var address } } &&
            address == counter ||
            ParseIndex(instructions, position + 1, end, counter) is not var (offset, next) || next >= end)
        {
            return null;
        }
        if (!span)
        {
            return instructions[next] is { Operation: CilOperation.LoadArrayElement, Operand: CilOperand.TypeIdentity { Value: var element } } &&
                PrimitiveElements.IsInteger(element)
                ? (new Access(source, null, element, offset), next + 1)
                : null;
        }
        if (next + 1 >= end || instructions[next].Operation != CilOperation.Call ||
            _calledMethods.Resolve(instructions[next]) is not
            {
                Definition: { Name: "get_Item", IsStatic: false },
                DeclaringType:
                {
                    Shape: CliTypeShape.GenericInstantiation,
                    ElementType.FullName: "System.Span`1" or "System.ReadOnlySpan`1",
                    TypeArguments: [var spanElement],
                },
            } indexer ||
            instructions[next + 1] is not { Operation: CilOperation.LoadObject, Operand: CilOperand.TypeIdentity { Value: var loaded } } ||
            !PrimitiveElements.IsInteger(loaded) || PrimitiveElements.Size(loaded) != PrimitiveElements.Size(spanElement))
        {
            return null;
        }
        return (new Access(source, indexer, loaded, offset), next + 2);
    }

    // ldloc i, or ldloc i; ldc c; add or sub.
    private static (int Offset, int Next)? ParseIndex(ImmutableArray<CilInstruction> instructions, int position, int end, int counter)
    {
        if (position >= end ||
            instructions[position] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } ||
            local != counter)
        {
            return null;
        }
        if (position + 2 < end &&
            instructions[position + 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var constant } } &&
            constant is > -MaximumOffset and < MaximumOffset &&
            instructions[position + 2].Operation is CilOperation.Add or CilOperation.Subtract)
        {
            return (instructions[position + 2].Operation == CilOperation.Add ? constant : -constant, position + 3);
        }
        return (0, position + 1);
    }

    // The bound pushes one int from constants, arguments, locals the loop does
    // not write, array lengths and span lengths, with additions and subtractions.
    private bool IsInvariantBound(CilInstruction[] bound, int counter, HashSet<int> locals)
    {
        if (bound.Length == 0) return false;
        var depth = 0;
        for (var index = 0; index < bound.Length; index++)
        {
            var instruction = bound[index];
            switch (instruction.Operation)
            {
                case CilOperation.LoadInt32 or CilOperation.LoadArgument:
                    depth++;
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (local == counter || locals.Contains(local)) return false;
                    depth++;
                    break;
                case CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress:
                    if (instruction is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var address } } &&
                        (address == counter || locals.Contains(address)) ||
                        index + 1 >= bound.Length || bound[index + 1].Operation != CilOperation.Call ||
                        _calledMethods.Resolve(bound[index + 1]) is not
                        {
                            Definition: { Name: "get_Length", IsStatic: false },
                            DeclaringType.ElementType.FullName: "System.Span`1" or "System.ReadOnlySpan`1",
                        })
                    {
                        return false;
                    }
                    depth++;
                    index++;
                    break;
                case CilOperation.Add or CilOperation.Subtract:
                    if (--depth < 1) return false;
                    break;
                case CilOperation.LoadArrayLength or CilOperation.ConvertInt32:
                    if (depth < 1) return false;
                    break;
                default:
                    return false;
            }
        }
        return depth == 1;
    }

    private List<CilInstruction> Prefix(int test, int counter, CilInstruction[] bound, List<Fold> folds,
        Dictionary<int, Access> copies)
    {
        var prefix = new List<CilInstruction>();
        void Add(CilOperation operation, CilOperand? operand = null) =>
            prefix.Add(new CilInstruction(0, 1, operation, operand ?? new CilOperand.None()));
        void Copy(CilInstruction instruction) => Add(instruction.Operation, instruction.Operand);
        void Bound() { foreach (var instruction in bound) Copy(instruction); }
        void Index(int offset)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(counter));
            if (offset == 0) return;
            Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(offset));
            Add(CilOperation.Add);
        }
        void Length(Access access)
        {
            Copy(access.Base);
            if (access.Indexer is { } indexer)
            {
                Add(CilOperation.Call, new CilOperand.MethodInstance(_spanLengths.Resolve(indexer)));
                return;
            }
            Add(CilOperation.LoadArrayLength);
            Add(CilOperation.ConvertInt32);
        }

        // i < end, and every access at i and at end - 1 within its array or span.
        Add(CilOperation.LoadLocal, new CilOperand.Index(counter));
        Bound();
        Add(CilOperation.BranchIfGreaterThanOrEqualSigned, new CilOperand.BranchTarget(test));
        foreach (var access in folds.Select(static fold => fold.Access).Concat(copies.Values)
                     .DistinctBy(static access => (access.Base.Operation, access.Base.Operand, access.Offset)))
        {
            if (access.Indexer is null)
            {
                Copy(access.Base);
                Add(CilOperation.BranchIfFalse, new CilOperand.BranchTarget(test));
            }
            Index(access.Offset);
            Length(access);
            Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, new CilOperand.BranchTarget(test));
            Bound();
            Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(access.Offset - 1));
            Add(CilOperation.Add);
            Length(access);
            Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, new CilOperand.BranchTarget(test));
        }

        // x = x op fold(range), as the statement applies one element.
        foreach (var fold in folds)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(fold.Local));
            Copy(fold.Access.Base);
            Index(fold.Access.Offset);
            if (fold.Access.Indexer is { } indexer)
            {
                Add(CilOperation.Call, new CilOperand.MethodInstance(indexer));
            }
            else
            {
                Add(CilOperation.LoadArrayElementAddress, new CilOperand.TypeIdentity(fold.Access.Element));
            }
            Bound();
            Add(CilOperation.LoadLocal, new CilOperand.Index(counter));
            Add(CilOperation.Subtract);
            Add(CilOperation.Call, new CilOperand.MethodInstance(_methods.Resolve(fold.Helper, fold.Lane)));
            foreach (var conversion in fold.Widening) Copy(conversion);
            Copy(fold.Combine);
            if (fold.Truncation is { } truncation) Copy(truncation);
            Add(CilOperation.StoreLocal, new CilOperand.Index(fold.Local));
        }

        // A copy keeps the last iteration's element, as the loop leaves it.
        foreach (var (local, access) in copies)
        {
            Copy(access.Base);
            Bound();
            Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(access.Offset - 1));
            Add(CilOperation.Add);
            if (access.Indexer is { } indexer)
            {
                Add(CilOperation.Call, new CilOperand.MethodInstance(indexer));
                Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(access.Element));
            }
            else
            {
                Add(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(access.Element));
            }
            Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        Bound();
        Add(CilOperation.StoreLocal, new CilOperand.Index(counter));
        return prefix;
    }

    private static bool IsLoad(CilInstruction instruction, int counter) =>
        instruction.Operation == CilOperation.LoadArgument ||
        instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } && local != counter;

    private static bool Writes(CilInstruction source, HashSet<int> locals) =>
        source is { Operation: CilOperation.LoadLocal or CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } } &&
        locals.Contains(local);

    private static bool IsIntegerConversion(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.ConvertInt32 or CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned } => true,
        { Operation: CilOperation.ConvertNumeric, Operand: CilOperand.NumericConversion { Checked: false, Native: false } } => true,
        _ => false,
    };
}
