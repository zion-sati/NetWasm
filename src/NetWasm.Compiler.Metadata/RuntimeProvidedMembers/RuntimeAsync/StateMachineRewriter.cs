// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync.StateMachineShapes;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IStateMachineRewriter
{
    // The runtime async body of an async method Roslyn compiled to a state machine: its MoveNext rewritten into the body
    // Roslyn writes with runtime-async=on, or, when either has a shape this does not take (or MoveNext is missing), the
    // kickoff's own body awaiting the task it returns.
    CilMethodBody Rewrite(MethodInstanceModel kickoff, CilMethodBody kickoffBody,
        (MethodInstanceModel Method, CilMethodBody Body)? moveNext);
}

// Roslyn's async state machine, mapped back to the method it was written as. The kickoff stores its arguments into the
// machine's fields, creates the builder, sets the state to -1 and starts it; MoveNext caches the state in a local,
// branches on it at the start of each protected block to the resume points, and runs each await as `GetAwaiter();
// stloc a; ldloca a; IsCompleted; brtrue K; [suspend: state = k; awaiter field = a; builder.Await(Unsafe)OnCompleted(ref
// a, ref this); leave] [resume: a = awaiter field; clear it; state = -1] K: ldloca a; GetResult()`, inside a try whose
// catch faults the builder, with the result going to SetResult after it. Rewritten: the parameter fields become the
// kickoff's arguments (as the kickoff stored them), the other fields locals, the state -1 where it is read (so each
// branch on it folds and the resume points go), each await `Await(awaitable)` for a Task, a ValueTask or a configured
// one (so a lowered caller's direct calls take it) and otherwise the awaiter's own UnsafeAwaitAwaiter or AwaitAwaiter
// after its IsCompleted test, SetResult a return of its value, and the catch that faults the builder gone: the runtime
// async lowering adds its own. Anything it does not recognize keeps the machine, the kickoff awaiting its task: a
// method is runtime async from its metadata on, before its body is read. The rewritten body's source offsets are
// MoveNext's, whose sequence points a kickoff's stack-trace locations are.
// Cost: a few linear passes over MoveNext's instructions (one forward pass finds each access's object), and a search of
// at most a few instructions back from each builder call.
internal sealed class StateMachineRewriter(
    Func<IRuntimeAsyncMemberResolver> members,
    IFieldRepository fields,
    ICilBodyLayout layout,
    IMetadataStackTypeResolver stackTypes) : IStateMachineRewriter
{

    private const string State = "<>1__state";
    private const string Builder = "<>t__builder";

    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);

    public CilMethodBody Rewrite(MethodInstanceModel kickoff, CilMethodBody kickoffBody,
        (MethodInstanceModel Method, CilMethodBody Body)? moveNext)
    {
        ArgumentNullException.ThrowIfNull(kickoff);
        ArgumentNullException.ThrowIfNull(kickoffBody);
        return (moveNext is var (method, body) ? Rewritten(kickoff, kickoffBody, WithoutNops(layout, method, body)) : null) ??
            Awaiting(_members.Value, layout, kickoff, kickoffBody);
    }

    private CilMethodBody? Rewritten(MethodInstanceModel kickoff, CilMethodBody kickoffBody, CilMethodBody moveNext)
    {
        if (kickoff.DeclaringType.IsValueType) return null;
        var machine = moveNext.Method.DeclaringType;
        if (KickoffArguments(kickoffBody, machine) is not { } arguments) return null;
        var instructions = moveNext.Instructions;
        if (instructions.Length < 2 || CilStackDepths.Compute(moveNext) is not { } depths) return null;
        // MoveNext caches the state in a local first, unless it has no await to resume at (an async method without one).
        var stateLocal = instructions.Length > 3 && IsThis(instructions[0]) &&
            IsMachineField(instructions[1], machine, CilOperation.LoadField, State) &&
            instructions[2] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var cached } }
                ? cached
                : -1;
        var positions = new Dictionary<int, int>(instructions.Length + 1);
        for (var index = 0; index < instructions.Length; index++) positions[instructions[index].Offset] = index;
        positions[instructions[^1].NextOffset] = instructions.Length;
        if (Producers(moveNext, depths) is not { } producers) return null;
        var rewrite = new Rewriting(instructions.Length);
        if (stateLocal >= 0) Remove(rewrite, 0, 3);

        // The catch that faults the builder: the region with the longest try, a catch around every other region.
        var outer = moveNext.ExceptionRegions
            .Where(region => region.Kind == CilExceptionRegionKind.Catch)
            .MaxBy(region => region.TryLength);
        if (outer is null ||
            moveNext.ExceptionRegions.Any(region => !ReferenceEquals(region, outer) &&
                (region.TryOffset < outer.TryOffset ||
                    (region.FilterOffset ?? region.HandlerOffset) + region.HandlerLength > outer.TryOffset + outer.TryLength)))
        {
            return null;
        }
        var handlerStart = positions[outer.HandlerOffset];
        var handlerEnd = positions[outer.HandlerOffset + outer.HandlerLength];
        var faults = false;
        for (var index = handlerStart; index < handlerEnd; index++) faults |= IsBuilderCall(instructions[index], "SetException");
        if (!faults) return null;
        Drop(rewrite, handlerStart, handlerEnd - handlerStart);

        // Each await, by its builder call; the temporaries a struct awaitable goes through.
        var temporaries = Temporaries(moveNext);
        for (var index = 0; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] ||
                !(IsBuilderCall(instructions[index], "AwaitUnsafeOnCompleted") || IsBuilderCall(instructions[index], "AwaitOnCompleted")))
            {
                continue;
            }
            if (!Await(_members.Value, moveNext, index, stateLocal, positions, temporaries, rewrite)) return null;
        }

        // The result: `ldarg.0; ldflda <>t__builder; [value]; call SetResult; ret` returns the value.
        for (var index = 0; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] || !IsBuilderCall(instructions[index], "SetResult")) continue;
            var address = producers[index];
            var next = index + 1;
            if (address < 0 || !IsMachineField(instructions[address], machine, CilOperation.LoadFieldAddress, Builder) ||
                producers[address] < 0 || !IsThis(instructions[producers[address]]) ||
                next >= instructions.Length || instructions[next].Operation != CilOperation.Return)
            {
                return null;
            }
            Remove(rewrite, producers[address], 1);
            Remove(rewrite, address, 1);
            Remove(rewrite, index, 1);
        }

        // The state: read as -1, and each test of it folded.
        for (var index = 0; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] ||
                instructions[index] is not
                {
                    Operation: CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index { Value: var local },
                } ||
                local != stateLocal)
            {
                continue;
            }
            if (instructions[index].Operation != CilOperation.LoadLocal || !FoldState(instructions, index, positions, rewrite))
                return null;
        }

        // The machine's fields: each access's object goes, and the access becomes an argument's, a local's, or the state's.
        var hoisted = new Dictionary<EntityKey, CilBodyLocal>();
        var consumed = new HashSet<int>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] || rewrite.Replaced.ContainsKey(index) ||
                instructions[index].Operation is not
                    (CilOperation.LoadField or CilOperation.LoadFieldAddress or CilOperation.StoreField) ||
                Field(instructions[index]) is not var (field, fieldType) || field.DeclaringType != machine)
            {
                continue;
            }
            var producer = producers[index];
            if (producer < 0 || !IsThis(instructions[producer]) || rewrite.Removed[producer] || !consumed.Add(producer) ||
                FieldAccess(field, fieldType, instructions[index].Operation, arguments, hoisted) is not { } access)
            {
                return null;
            }
            Remove(rewrite, producer, 1);
            rewrite.Replaced[index] = access;
        }
        for (var index = 0; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] || rewrite.Replaced.ContainsKey(index)) continue;
            if (IsThis(instructions[index]) ||
                instructions[index].Operation is CilOperation.LoadArgumentAddress or CilOperation.StoreArgument)
            {
                return null;
            }
        }

        return Lay(kickoff, moveNext, outer, rewrite, hoisted, positions);
    }

    // The kickoff: each statement stores into the machine (a struct local's address or a class local) an argument, the
    // builder's Create() or the state -1; then it starts the builder and returns its task. The arguments by field.
    private Dictionary<EntityKey, int>? KickoffArguments(CilMethodBody body, EntityKey machine)
    {
        var instructions = body.Instructions;
        var arguments = new Dictionary<EntityKey, int>();
        var started = false;
        var index = 0;
        if (instructions.Length > 1 && instructions[0].Operation == CilOperation.NewObject &&
            instructions[1].Operation == CilOperation.StoreLocal)
        {
            index = 2;
        }
        while (index < instructions.Length)
        {
            if (instructions[index].Operation == CilOperation.Return)
                return started && index == instructions.Length - 1 ? arguments : null;
            if (instructions[index].Operation is not (CilOperation.LoadLocalAddress or CilOperation.LoadLocal) ||
                index + 2 >= instructions.Length)
            {
                return null;
            }
            var value = instructions[index + 1];
            var store = instructions[index + 2];
            if (store.Operation == CilOperation.StoreField && Field(store) is var (field, _) && field.DeclaringType == machine)
            {
                if (value is { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } })
                    arguments[field.Key] = argument;
                else if (!(value.Operation == CilOperation.LoadInt32 || IsBuilderCall(value, "Create")))
                    return null;
                index += 3;
                continue;
            }
            // `ldloca m; ldflda <>t__builder; ldloca m; call Start` and `ldloca m; ldflda <>t__builder; call get_Task`.
            if (IsMachineField(value, machine, CilOperation.LoadFieldAddress, Builder))
            {
                if (IsBuilderCall(store, "get_Task"))
                {
                    if (!started) return null;
                    index += 3;
                    continue;
                }
                if (index + 3 < instructions.Length &&
                    store.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadLocal &&
                    IsBuilderCall(instructions[index + 3], "Start"))
                {
                    started = true;
                    index += 4;
                    continue;
                }
            }
            return null;
        }
        return null;
    }

    // What a field access of the machine becomes, its object already gone.
    private static List<CilBodyElement>? FieldAccess(FieldDefinitionModel field, CliTypeIdentity fieldType,
        CilOperation operation, Dictionary<EntityKey, int> arguments, Dictionary<EntityKey, CilBodyLocal> hoisted)
    {
        var name = field.Name;
        if (name == State)
        {
            return operation switch
            {
                CilOperation.LoadField => [Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(-1))],
                CilOperation.StoreField => [Op(CilOperation.Pop)],
                _ => null,
            };
        }
        if (name == Builder || name.StartsWith("<>u__", StringComparison.Ordinal)) return null;
        if (arguments.TryGetValue(field.Key, out var argument))
        {
            return
            [
                Op(operation switch
                {
                    CilOperation.LoadField => CilOperation.LoadArgument,
                    CilOperation.LoadFieldAddress => CilOperation.LoadArgumentAddress,
                    _ => CilOperation.StoreArgument,
                }, new CilOperand.Index(argument)),
            ];
        }
        if (!hoisted.TryGetValue(field.Key, out var local))
            hoisted[field.Key] = local = new CilBodyLocal(fieldType);
        return
        [
            new CilBodyElement.LocalInstruction(operation switch
            {
                CilOperation.LoadField => CilOperation.LoadLocal,
                CilOperation.LoadFieldAddress => CilOperation.LoadLocalAddress,
                _ => CilOperation.StoreLocal,
            }, local),
        ];
    }

    // The draft's elements, laid out as the kickoff's body.
    private CilMethodBody? Lay(MethodInstanceModel kickoff, CilMethodBody moveNext, CilExceptionRegion outer,
        Rewriting rewrite, Dictionary<EntityKey, CilBodyLocal> hoisted, Dictionary<int, int> positions)
    {
        var reading = CilBodyDrafts.Read(moveNext);
        var labels = reading.Labels;
        var offsets = new Dictionary<CilBodyLabel, int>(ReferenceEqualityComparer.Instance);
        foreach (var (offset, label) in labels) offsets[label] = offset;
        var locals = reading.Draft.Locals;
        var elements = new List<CilBodyElement>(reading.Draft.Elements.Length);
        var index = -1;
        foreach (var element in reading.Draft.Elements)
        {
            if (element is CilBodyElement.Mark mark)
            {
                index = positions[offsets[mark.Label]];
                elements.Add(mark);
                continue;
            }
            // MoveNext's offsets stay the instructions' sources, so stack traces and line tables read MoveNext's
            // sequence points, which the kickoff's symbols are (StackTraceSourceLocationReader); a replacement reports
            // the instruction it replaces.
            if (rewrite.Replaced.TryGetValue(index, out var replacement))
            {
                elements.AddRange(replacement.Select(item => Labeled(item, locals, labels) with { Source = element.Source }));
                continue;
            }
            if (!rewrite.Removed[index]) elements.Add(element);
        }
        var regions = reading.Draft.Regions
            .Where(region => !ReferenceEquals(region.TryStart, labels[outer.TryOffset]) ||
                !ReferenceEquals(region.HandlerStart, labels[outer.HandlerOffset]))
            .ToImmutableArray();
        Unwrap(elements, regions);
        var reached = Reachable(elements, regions).ToImmutableArray();
        // A branch control still reaches into what went would run something else (the dispatch's own branches, which
        // the folded state leaves unreached, may).
        foreach (var element in reached)
        {
            var named = element switch
            {
                CilBodyElement.Branch branch => [branch.Target],
                CilBodyElement.Switch table => table.Targets,
                _ => ImmutableArray<CilBodyLabel>.Empty,
            };
            foreach (var label in named)
            {
                var position = positions[offsets[label]];
                if (position < rewrite.Blocked.Length && rewrite.Blocked[position]) return null;
            }
        }
        var draft = new CilBodyDraft([.. locals, .. hoisted.Values], reached, regions)
        {
            InitializesLocals = true,
        };
        return layout.Lay(draft, kickoff, moveNext.MaxStack + 2);
    }

    private bool IsMachineField(CilInstruction instruction, EntityKey machine, CilOperation operation, string name) =>
        instruction.Operation == operation && Field(instruction) is var (field, _) &&
        field.DeclaringType == machine && field.Name == name;

    // A field instruction's field and its type as a stack type (an enum from another assembly is its underlying
    // integer): a generic type's field is an instance (its type substituted), any other the field's own definition.
    private (FieldDefinitionModel Definition, CliTypeIdentity Type)? Field(CilInstruction instruction) => instruction.Operand switch
    {
        CilOperand.FieldInstance { Value: var field } => (field.Definition, stackTypes.Resolve(field.FieldType)),
        CilOperand.Entity { Key: var key } when instruction.Operation is CilOperation.LoadField or CilOperation.LoadFieldAddress or
            CilOperation.StoreField => fields.GetField(key) is var definition
                ? (definition, stackTypes.Resolve(definition.SignatureType))
                : null,
        _ => null,
    };

}
