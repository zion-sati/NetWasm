using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IArrayLoopIdiomRewriter
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// Roslyn's `for (...; j < end; j++) array[j] = value;` and
// `for (...; j < end; j++) destination[j] = source[j];` store one element per
// iteration. Ahead of the loop's entry jump, ArrayLoops.Fill or Copy does every
// iteration from j that stays within the arrays and sets j to where the loop
// goes on from: end, so the loop only tests, or the first iteration that
// throws, which the loop then runs as before. The loop body holds nothing but
// the store and the increment, so nothing else can change the arrays, the value
// or end; they are constants, arguments or locals other than j, and the
// elements are primitives. `j <= C` with a constant C below int.MaxValue ends
// at C + 1.
internal sealed class ArrayLoopIdiomRewriter(IArrayLoopMethodResolver methods, bool enabled) : IArrayLoopIdiomRewriter
{
    private readonly IArrayLoopMethodResolver _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction => instruction.Operation == CilOperation.StoreArrayElement))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        List<CilInstruction>[]? replacements = null;
        for (var jump = 0; jump < instructions.Length; jump++)
        {
            if (instructions[jump] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var test } } ||
                !indices.TryGetValue(test, out var condition) || condition <= jump ||
                Match(instructions, jump, condition) is not { } call)
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[jump] = [.. call, instructions[jump]];
        }
        // A call takes four arguments above whatever the jump leaves.
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with { MaxStack = body.MaxStack + 4 };
    }

    // The instructions that run the loop's in-bounds iterations ahead of its
    // entry jump and store the next index to the counter.
    private List<CilInstruction>? Match(ImmutableArray<CilInstruction> instructions, int jump, int condition)
    {
        if (instructions[condition] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var counter } } ||
            End(instructions, condition + 1, counter) is not var (end, branch) ||
            instructions[branch].Operand is not CilOperand.BranchTarget { Offset: var start } ||
            start != instructions[jump + 1].Offset)
        {
            return null;
        }
        // The increment ends the body, right before the test.
        var increment = condition - 4;
        if (increment <= jump ||
            instructions[increment] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } ||
            read != counter ||
            instructions[increment + 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 1 } } ||
            instructions[increment + 2].Operation != CilOperation.Add ||
            instructions[increment + 3] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var written } } ||
            written != counter)
        {
            return null;
        }
        var store = increment - 1;
        if (instructions[store] is not { Operation: CilOperation.StoreArrayElement, Operand: CilOperand.TypeIdentity { Value: var element } } ||
            !IsPrimitive(element))
        {
            return null;
        }
        var counterLoad = new CilInstruction(0, 1, CilOperation.LoadLocal, new CilOperand.Index(counter));
        var storeCounter = new CilInstruction(0, 1, CilOperation.StoreLocal, new CilOperand.Index(counter));
        // array[j] = value
        if (store - 3 == jump + 1 &&
            IsArray(instructions[jump + 1], counter) && IsCounter(instructions[jump + 2], counter) &&
            IsValue(instructions[jump + 3], counter, element))
        {
            return
            [
                Copy(instructions[jump + 1]), counterLoad, .. end, Copy(instructions[jump + 3]),
                Call(_methods.Fill(element)), storeCounter,
            ];
        }
        // destination[j] = source[j]
        if (store - 5 == jump + 1 &&
            IsArray(instructions[jump + 1], counter) && IsCounter(instructions[jump + 2], counter) &&
            IsArray(instructions[jump + 3], counter) && IsCounter(instructions[jump + 4], counter) &&
            instructions[jump + 5] is { Operation: CilOperation.LoadArrayElement, Operand: CilOperand.TypeIdentity { Value: var loaded } } &&
            IsPrimitive(loaded) && loaded.StackKind == element.StackKind && PrimitiveElements.Size(loaded) is { } size && PrimitiveElements.Size(element) == size)
        {
            return
            [
                Copy(instructions[jump + 1]), Copy(instructions[jump + 3]), counterLoad, .. end,
                Call(_methods.Copy(element)), storeCounter,
            ];
        }
        return null;
    }

    // The test's bound, as instructions that push end, and the index of its
    // branch back to the body.
    private static (CilInstruction[] End, int Branch)? End(ImmutableArray<CilInstruction> instructions, int bound, int counter)
    {
        if (bound + 3 < instructions.Length &&
            IsArray(instructions[bound], counter) &&
            instructions[bound + 1].Operation == CilOperation.LoadArrayLength &&
            instructions[bound + 2].Operation == CilOperation.ConvertInt32 &&
            instructions[bound + 3].Operation == CilOperation.BranchIfLessThanSigned)
        {
            return ([Copy(instructions[bound]), Copy(instructions[bound + 1]), Copy(instructions[bound + 2])], bound + 3);
        }
        if (bound + 1 >= instructions.Length) return null;
        var branch = instructions[bound + 1].Operation;
        if (branch == CilOperation.BranchIfLessThanSigned &&
            (instructions[bound].Operation == CilOperation.LoadInt32 || IsArray(instructions[bound], counter)))
        {
            return ([Copy(instructions[bound])], bound + 1);
        }
        if (branch == CilOperation.BranchIfLessThanOrEqualSigned &&
            instructions[bound].Operand is CilOperand.ConstantI4 { Value: < int.MaxValue and var last } &&
            instructions[bound].Operation == CilOperation.LoadInt32)
        {
            return ([new CilInstruction(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(last + 1))], bound + 1);
        }
        return null;
    }

    private static bool IsCounter(CilInstruction instruction, int counter) =>
        instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } && local == counter;

    private static bool IsArray(CilInstruction instruction, int counter) =>
        instruction.Operation == CilOperation.LoadArgument ||
        instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } && local != counter;

    // A constant of the element's own stack type, or a local or argument.
    private static bool IsValue(CilInstruction instruction, int counter, CliTypeIdentity element) =>
        (instruction.Operation, element.StackKind) is
            (CilOperation.LoadInt32, CliValueKind.I4) or (CilOperation.LoadInt64, CliValueKind.I8) or
            (CilOperation.LoadFloat32, CliValueKind.F4) or (CilOperation.LoadFloat64, CliValueKind.F8) ||
        IsArray(instruction, counter);

    private static bool IsPrimitive(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.Primitive or CliTypeShape.Named && type.IsValueType &&
        type.StackKind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt;

    private static CilInstruction Copy(CilInstruction instruction) =>
        new(0, 1, instruction.Operation, instruction.Operand);

    private static CilInstruction Call(MethodInstanceModel method) =>
        new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(method));
}
