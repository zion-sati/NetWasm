using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IPureCallReuser
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A pure Math or MathF call made again on the same arguments, as in Benchstone's
// `Math.Sin(s_x) * Math.Sin(s_x)`, gives the same value, so the first call keeps
// its result in a new local and the repeat reads the local. Pure is the loop
// hoister's: a static floating function of floating arguments, with no effects
// and no exceptions. The repeat's arguments must be the same instructions as the
// first call's, built from constants, locals, arguments, static fields,
// arithmetic that cannot throw and other pure calls, and nothing between the two
// calls may change what they read: no jump lands between them, no local or
// argument they read is stored or has its address taken, and the instructions
// between only compute values, read the same static fields or branch away. This
// runs before inlining, while the calls are still calls.
internal sealed class PureCallReuser(ICalledMethodResolver calledMethods, bool enabled) : IPureCallReuser
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var instructions = body.Instructions;
        // Each pure call with its arguments' first instruction and the variables they read.
        var windows = new Dictionary<int, (int First, Reads Reads)>();
        for (var call = 0; call < instructions.Length; call++)
        {
            if (instructions[call].Operation == CilOperation.Call &&
                _calledMethods.Resolve(instructions[call]) is { } target && LoopInvariantCallHoister.IsPure(target) &&
                Window(instructions, call, target.Signature.ParameterTypes.Length) is { } window)
            {
                windows.Add(call, window);
            }
        }
        if (windows.Count < 2) return body;

        var targets = instructions
            .Select(static instruction => instruction.Operand)
            .OfType<CilOperand.BranchTarget>()
            .Select(static target => target.Offset)
            .Concat(body.ExceptionRegions.SelectMany(static region => (int?[])
                [region.TryOffset, region.HandlerOffset, region.FilterOffset]).OfType<int>())
            .ToHashSet();
        var addressed = ExposedSlots.Of(instructions);
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        List<CilInstruction>[]? replacements = null;
        var claimed = new HashSet<int>();
        // Longer windows first, so a repeated call around a repeated call is reused whole.
        foreach (var (call, (first, reads)) in windows.OrderByDescending(pair => pair.Key - pair.Value.First).ThenBy(pair => pair.Key))
        {
            if (Enumerable.Range(first, call - first + 1).Any(claimed.Contains) ||
                reads.Variables.Any(addressed.Contains))
            {
                continue;
            }
            int? local = null;
            for (var next = call + 1; next < instructions.Length && !targets.Contains(instructions[next].Offset); next++)
            {
                if (windows.TryGetValue(next, out var repeat) && repeat.First > call &&
                    !Enumerable.Range(repeat.First, next - repeat.First + 1).Any(claimed.Contains) &&
                    Same(instructions, first, call, repeat.First, next))
                {
                    replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
                    if (local is null)
                    {
                        var target = _calledMethods.Resolve(instructions[call])!;
                        var type = CliTypeIdentity.Primitive(target.Signature.ReturnType == CliValueKind.F4 ? "f4" : "f8",
                            target.Signature.ReturnType);
                        locals.Add(type.StackKind);
                        localTypes.Add(type);
                        local = locals.Count - 1;
                    }
                    for (var index = repeat.First; index <= next; index++)
                    {
                        replacements[index] = [];
                        claimed.Add(index);
                    }
                    replacements[repeat.First] = [new CilInstruction(0, 1, CilOperation.LoadLocal, new CilOperand.Index(local.Value))];
                    continue;
                }
                if (!Unchanging(instructions[next], reads)) break;
            }
            if (local is not null)
            {
                claimed.UnionWith(Enumerable.Range(first, call - first + 1));
                replacements![call] =
                [
                    instructions[call],
                    new CilInstruction(0, 1, CilOperation.Duplicate, new CilOperand.None()),
                    new CilInstruction(0, 1, CilOperation.StoreLocal, new CilOperand.Index(local.Value)),
                ];
            }
        }
        return replacements is null
            ? body
            : RenumberedInstructions.Splice(body, replacements) with
            {
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
    }

    private sealed record Reads(HashSet<(bool Argument, int Index)> Variables, HashSet<object> Fields);

    // The first instruction of the call's arguments, when constants, locals,
    // arguments, static fields, arithmetic that cannot throw and other pure calls
    // compute them, with what they read; null otherwise. A pure call takes an
    // argument or more, which instructions ahead of it compute.
    private (int First, Reads Reads)? Window(ImmutableArray<CilInstruction> instructions, int call, int count)
    {
        var reads = new Reads([], []);
        var needed = count;
        for (var index = call - 1; ; index--)
        {
            var instruction = instructions[index];
            int pushes, pops;
            switch (instruction.Operation)
            {
                case CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64:
                    (pushes, pops) = (1, 0);
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    reads.Variables.Add((false, local));
                    (pushes, pops) = (1, 0);
                    break;
                case CilOperation.LoadArgument when instruction.Operand is CilOperand.Index { Value: var argument }:
                    reads.Variables.Add((true, argument));
                    (pushes, pops) = (1, 0);
                    break;
                case CilOperation.LoadStaticField:
                    reads.Fields.Add(Key(instruction.Operand));
                    (pushes, pops) = (1, 0);
                    break;
                // Floating arithmetic: the operands are a pure call's arguments.
                case CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide:
                    (pushes, pops) = (1, 2);
                    break;
                case CilOperation.Negate or CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64:
                    (pushes, pops) = (1, 1);
                    break;
                case CilOperation.Call when _calledMethods.Resolve(instruction) is { } target &&
                    LoopInvariantCallHoister.IsPure(target):
                    (pushes, pops) = (1, target.Signature.ParameterTypes.Length);
                    break;
                default:
                    return null;
            }
            needed += pops - pushes;
            if (needed == 0) return (index, reads);
        }
    }

    // Whether an instruction between a call and its repeat leaves what the call
    // reads as it was: it computes a value, reads a static field the call reads,
    // stores a local the call does not read, or branches away.
    private bool Unchanging(CilInstruction instruction, Reads reads) => instruction.Operation switch
    {
        CilOperation.Nop or CilOperation.LoadLocal or CilOperation.LoadArgument or CilOperation.LoadNull or
            CilOperation.Duplicate or CilOperation.Pop or
            >= CilOperation.LoadInt32 and <= CilOperation.LoadFloat64 or
            >= CilOperation.Add and <= CilOperation.RemainderUnsigned or
            >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned or
            >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric or
            CilOperation.Negate or CilOperation.OnesComplement => true,
        CilOperation.LoadStaticField => reads.Fields.Contains(Key(instruction.Operand)),
        CilOperation.StoreLocal or CilOperation.StoreArgument =>
            !reads.Variables.Contains((instruction.Operation == CilOperation.StoreArgument, ((CilOperand.Index)instruction.Operand).Value)),
        CilOperation.Call => LoopInvariantCallHoister.IsPure(_calledMethods.Resolve(instruction)!),
        _ => IsBranchAway(instruction),
    };

    // A conditional branch leaves the path without changing anything on it.
    private static bool IsBranchAway(CilInstruction instruction) =>
        instruction.Operand is CilOperand.BranchTarget && instruction.Operation != CilOperation.Leave;

    private static bool Same(ImmutableArray<CilInstruction> instructions, int first, int call, int repeatFirst, int repeat) =>
        call - first == repeat - repeatFirst &&
        Enumerable.Range(0, call - first + 1).All(offset =>
            instructions[first + offset].Operation == instructions[repeatFirst + offset].Operation &&
            Equals(Key(instructions[first + offset].Operand), Key(instructions[repeatFirst + offset].Operand)));

    // Methods and fields compare by name: their models hold arrays, which compare by reference.
    private static object Key(CilOperand operand) => operand switch
    {
        CilOperand.MethodInstance { Value: var method } => method.CanonicalName,
        CilOperand.FieldInstance { Value: var field } => field.CanonicalName,
        _ => operand,
    };
}
