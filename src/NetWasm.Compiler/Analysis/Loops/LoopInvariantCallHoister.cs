using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface ILoopInvariantCallHoister
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A loop that calls a pure Math or MathF function of values it does not change,
// such as Burgers' `Math.Pow(nu * dt / dx, 2.0)`, computes the call once ahead
// of its entry jump into a new local and reads the local instead. Pure means a
// static floating function of floating arguments: deterministic, with no
// effects and no exceptions, so one call, even ahead of a loop that runs no
// iteration, gives every iteration's value. The arguments are constants,
// arguments and locals the loop does not write, combined by arithmetic that
// cannot throw: no integer division. This runs before inlining, while the call
// is still a call, so an inlined body cannot leave its branches in the loop and
// the loop stays one straight-line body for the vectorizer.
internal sealed class LoopInvariantCallHoister(ICalledMethodResolver calledMethods, bool enabled) : ILoopInvariantCallHoister
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(instruction => instruction.Operation == CilOperation.Call &&
                _calledMethods.Resolve(instruction) is { } target && IsPure(target)))
        {
            return body;
        }

        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var targets = instructions
            .SelectMany(static instruction => instruction.Operand switch
            {
                CilOperand.BranchTarget target => [target.Offset],
                CilOperand.SwitchTargets switches => switches.Offsets,
                _ => ImmutableArray<int>.Empty,
            })
            .Concat(body.ExceptionRegions.SelectMany(static region => (int?[])
                [region.TryOffset, region.HandlerOffset, region.FilterOffset]).OfType<int>())
            .ToHashSet();
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        List<CilInstruction>[]? replacements = null;
        var claimed = new HashSet<int>();
        for (var jump = 0; jump < instructions.Length; jump++)
        {
            // A loop Roslyn enters through a jump to its test: the body runs from
            // the jump's successor to the backward branch at the test's end.
            if (instructions[jump] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var test } } ||
                !indices.TryGetValue(test, out var condition) || condition <= jump + 1 ||
                jump + 1 >= instructions.Length)
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
            if (end < 0) continue;
            var written = Written(instructions, jump + 1, end);

            List<CilInstruction>? prefix = null;
            for (var call = jump + 1; call < end; call++)
            {
                if (claimed.Contains(call) || instructions[call].Operation != CilOperation.Call ||
                    _calledMethods.Resolve(instructions[call]) is not { } target || !IsPure(target) ||
                    Window(instructions, body, jump + 1, call, target.Signature.ParameterTypes.Length, written) is not { } first ||
                    Enumerable.Range(first + 1, call - first).Any(index => targets.Contains(instructions[index].Offset)) ||
                    Enumerable.Range(first, call - first + 1).Any(claimed.Contains))
                {
                    continue;
                }
                replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
                prefix ??= [];
                var type = CliTypeIdentity.Primitive(target.Signature.ReturnType == CliValueKind.F4 ? "f4" : "f8",
                    target.Signature.ReturnType);
                locals.Add(type.StackKind);
                localTypes.Add(type);
                var local = locals.Count - 1;
                for (var index = first; index <= call; index++)
                {
                    prefix.Add(new CilInstruction(0, 1, instructions[index].Operation, instructions[index].Operand));
                    replacements[index] = [];
                    claimed.Add(index);
                }
                prefix.Add(new CilInstruction(0, 1, CilOperation.StoreLocal, new CilOperand.Index(local)));
                // The first instruction keeps its offset, so a jump to it still lands.
                replacements[first] = [new CilInstruction(0, 1, CilOperation.LoadLocal, new CilOperand.Index(local))];
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
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            };
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

    // The first instruction of the call's arguments, when they are computed
    // from scratch by constants, unwritten locals and arguments, arithmetic that
    // cannot throw and other pure calls; null otherwise.
    private int? Window(ImmutableArray<CilInstruction> instructions, CilMethodBody body, int start, int call, int count,
        HashSet<(bool Argument, int Index)> written)
    {
        var needed = count;
        for (var index = call - 1; index >= start && needed > 0; index--)
        {
            var instruction = instructions[index];
            int pushes, pops;
            switch (instruction.Operation)
            {
                case CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64:
                    (pushes, pops) = (1, 0);
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (written.Contains((false, local))) return null;
                    (pushes, pops) = (1, 0);
                    break;
                case CilOperation.LoadArgument when instruction.Operand is CilOperand.Index { Value: var argument }:
                    if (written.Contains((true, argument))) return null;
                    (pushes, pops) = (1, 0);
                    break;
                case CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply:
                    (pushes, pops) = (1, 2);
                    break;
                case CilOperation.Divide:
                    // Only a floating division, which cannot throw.
                    if (Kind(instructions, body, index - 1) is not (CliValueKind.F4 or CliValueKind.F8)) return null;
                    (pushes, pops) = (1, 2);
                    break;
                case CilOperation.Negate or CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64:
                    (pushes, pops) = (1, 1);
                    break;
                case CilOperation.Call when _calledMethods.Resolve(instruction) is { } target && IsPure(target):
                    (pushes, pops) = (1, target.Signature.ParameterTypes.Length);
                    break;
                default:
                    return null;
            }
            needed += pops - pushes;
            if (needed == 0) return index;
        }
        return null;
    }

    // The kind an instruction leaves on top of the stack, for the operands of a
    // division; Unknown when that takes more than the instruction itself.
    private CliValueKind Kind(ImmutableArray<CilInstruction> instructions, CilMethodBody body, int index) =>
        instructions[index] switch
        {
            { Operation: CilOperation.LoadFloat32 or CilOperation.ConvertFloat32 } => CliValueKind.F4,
            { Operation: CilOperation.LoadFloat64 or CilOperation.ConvertFloat64 } => CliValueKind.F8,
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
                when (uint)local < (uint)body.Locals.Length => body.Locals[local],
            { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } } =>
                Argument(body, argument),
            { Operation: CilOperation.Call } when _calledMethods.Resolve(instructions[index]) is { } target =>
                target.Signature.ReturnType,
            {
                Operation: CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide or
                CilOperation.Negate
            } => Kind(instructions, body, index - 1),
            _ => CliValueKind.Unknown,
        };

    private static CliValueKind Argument(CilMethodBody body, int argument)
    {
        var parameters = body.Method.Signature.ParameterTypes;
        var index = body.Method.IsStatic ? argument : argument - 1;
        return (uint)index < (uint)parameters.Length ? parameters[index] : CliValueKind.Unknown;
    }

    // Math and MathF functions of floating values.
    internal static bool IsPure(MethodInstanceModel method) =>
        method.Definition.IsStatic &&
        method.DeclaringType.FullName is "System.Math" or "System.MathF" &&
        method.Signature.ReturnType is CliValueKind.F4 or CliValueKind.F8 &&
        method.Signature.ParameterTypes.All(static type => type is CliValueKind.F4 or CliValueKind.F8);
}
