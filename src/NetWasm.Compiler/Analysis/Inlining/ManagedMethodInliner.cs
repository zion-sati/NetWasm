using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal sealed class ManagedMethodInliner(
    IManagedInliningResolver resolver,
    ITerminalContinuationInliningPlanner terminalContinuations) :
    IManagedMethodInliner
{
    private readonly IManagedInliningResolver _resolver = resolver ??
        throw new ArgumentNullException(nameof(resolver));
    private readonly ITerminalContinuationInliningPlanner _terminalContinuations =
        terminalContinuations ??
        throw new ArgumentNullException(nameof(terminalContinuations));

    // The budgets keep each method far below engine function and local limits
    // and keep compilation time proportional to the input.
    internal const int MaximumInstructions = 4096;
    internal const int MaximumLocals = 1024;

    public CilMethodBody Inline(
        MethodInstanceModel caller,
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks,
        bool nested = false)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(instructionEntryStacks);
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var expanded = new List<PendingInstruction>(body.Instructions.Length);
        var offsets = new Dictionary<int, int>();
        var producers = new StackSlotProducers(body, instructionEntryStacks);
        // Stores that receive a value constructed directly in their local.
        var constructed = new HashSet<int>();
        var maximumInlineStack = 0;
        var changed = false;
        var loops = LoopSpans(body);
        var exposed = body.Instructions
            .Where((instruction, index) => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress &&
                !ReadsFieldOnly(body, index))
            .Select(static instruction => (instruction.Operation == CilOperation.LoadArgumentAddress,
                ((CilOperand.Index)instruction.Operand).Value))
            .ToHashSet();

        for (var callerIndex = 0;
             callerIndex < body.Instructions.Length;
             callerIndex++)
        {
            var instruction = body.Instructions[callerIndex];
            offsets.Add(instruction.Offset, expanded.Count);
            if (constructed.Contains(callerIndex))
            {
                expanded.Add(new(instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() },
                    RemapCallerBranch: true));
                continue;
            }
            // A constrained call passes its receiver by address.
            var plan = instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject) ||
                callerIndex > 0 && body.Instructions[callerIndex - 1].Operation == CilOperation.Constrained
                ? null
                : _resolver.Resolve(new(caller, instruction)
                {
                    Nested = nested,
                    InLoop = loops.Any(loop => loop.Start <= instruction.Offset && instruction.Offset <= loop.End),
                });
            if (plan is null ||
                expanded.Count + body.Instructions.Length - callerIndex + plan.Body.Instructions.Length > MaximumInstructions ||
                locals.Count + GetArgumentTypes(plan.Method).Length + plan.Body.Locals.Length + 1 > MaximumLocals ||
                instruction.Operation == CilOperation.NewObject && WritesArgument(plan.Body, 0))
            {
                expanded.Add(new(instruction, RemapCallerBranch: true));
                continue;
            }

            CilInstruction? receiver = null;
            int? instance = null;
            var pushesInstance = true;
            if (instruction.Operation == CilOperation.NewObject)
            {
                // The value is constructed in the local it is stored to, as
                // Roslyn does for a declaration, or else in a fresh local; the
                // body reaches it by address.
                if (Destination(body, callerIndex, plan.Method.DeclaringType, producers) is { } store)
                {
                    constructed.Add(store);
                    instance = ((CilOperand.Index)body.Instructions[store].Operand).Value;
                    pushesInstance = false;
                }
                else
                {
                    instance = locals.Count;
                    locals.Add(plan.Method.DeclaringType.StackKind);
                    localTypes.Add(plan.Method.DeclaringType);
                }
                receiver = CreateSynthetic(instruction, CilOperation.LoadLocalAddress, new CilOperand.Index(instance.Value));
            }
            else if (!plan.Method.Definition.IsStatic && plan.Method.DeclaringType.IsValueType)
            {
                // A value-type method receives its instance by address.
                var depth = instructionEntryStacks.TryGetValue(instruction.Offset, out var stack)
                    ? stack.Length - GetArgumentTypes(plan.Method).Length
                    : -1;
                if (depth < 0 || stack[depth] != CliValueKind.ManagedAddress)
                {
                    expanded.Add(new(instruction, RemapCallerBranch: true));
                    continue;
                }
                // A local's or argument's address never changes, so the body
                // reads it directly instead of through a spilled copy, and the
                // instance stays a plain local.
                if (producers.Find(callerIndex, depth) is { } producer &&
                    body.Instructions[producer].Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress &&
                    !WritesArgument(plan.Body, 0))
                {
                    receiver = body.Instructions[producer];
                    expanded[offsets[receiver.Offset]] = new(
                        receiver with { Operation = CilOperation.Nop, Operand = new CilOperand.None() },
                        RemapCallerBranch: true);
                }
            }

            var forwarded = ForwardedArguments(body, callerIndex, caller, plan, receiver is not null, instance,
                exposed, producers, instructionEntryStacks);
            foreach (var load in forwarded.Values)
            {
                expanded[offsets[load.Offset]] = new(
                    load with { Operation = CilOperation.Nop, Operand = new CilOperand.None() },
                    RemapCallerBranch: true);
            }

            // A continuation would move caller instructions across exception
            // region boundaries, and would not push a constructed value.
            var terminalContinuation = !body.ExceptionRegions.IsEmpty || instance is not null ? null : _terminalContinuations.Plan(new(
                body,
                callerIndex,
                plan,
                instructionEntryStacks));
            if (terminalContinuation is null)
            {
                Inline(
                    instruction,
                    plan,
                    receiver,
                    forwarded,
                    instance,
                    pushesInstance,
                    body.ExceptionRegions.Any(region => region.TryOffset == instruction.NextOffset),
                    locals,
                    localTypes,
                    expanded);
            }
            else
            {
                InlineTerminalContinuation(
                    instruction,
                    plan,
                    receiver,
                    forwarded,
                    terminalContinuation,
                    locals,
                    localTypes,
                    expanded);
                callerIndex = terminalContinuation.ConsumedThroughIndex;
            }
            maximumInlineStack = Math.Max(maximumInlineStack, plan.Body.MaxStack + (instance is null ? 0 : 1));
            changed = true;
        }

        if (!changed)
        {
            return body;
        }

        offsets.Add(body.Instructions[^1].NextOffset, expanded.Count);
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(expanded.Count);
        for (var index = 0; index < expanded.Count; index++)
        {
            var pending = expanded[index];
            instructions.Add(pending.Instruction with
            {
                Offset = index,
                NextOffset = index + 1,
                OriginalOffset = pending.Instruction.SourceOffset,
                Operand = pending.RemapCallerBranch
                    ? Remap(pending.Instruction.Operand, offsets)
                    : pending.Instruction.Operand,
            });
        }

        return body with
        {
            MaxStack = body.MaxStack + maximumInlineStack,
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            Instructions = instructions.MoveToImmutable(),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => Remap(region, offsets))],
        };
    }

    // Callees carry no exception regions, so inlined code stays inside the
    // region of the call it replaces and each boundary maps to its instruction.
    private static CilExceptionRegion Remap(CilExceptionRegion region, IReadOnlyDictionary<int, int> offsets)
    {
        var tryOffset = ResolveOffset(region.TryOffset, offsets);
        var handlerOffset = ResolveOffset(region.HandlerOffset, offsets);
        return region with
        {
            TryOffset = tryOffset,
            TryLength = ResolveOffset(region.TryOffset + region.TryLength, offsets) - tryOffset,
            HandlerOffset = handlerOffset,
            HandlerLength = ResolveOffset(region.HandlerOffset + region.HandlerLength, offsets) - handlerOffset,
            FilterOffset = region.FilterOffset is { } filter ? ResolveOffset(filter, offsets) : null,
        };
    }

    private static void InlineTerminalContinuation(
        CilInstruction call,
        ManagedInliningPlan plan,
        CilInstruction? receiver,
        IReadOnlyDictionary<int, CilInstruction> forwarded,
        TerminalContinuationInliningPlan continuation,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        List<PendingInstruction> expanded)
    {
        var (argumentStart, calleeLocalStart) = AppendInlineLocals(
            plan,
            locals,
            localTypes);
        AppendArgumentStores(call, plan.Method, argumentStart, receiver is not null, forwarded, expanded);

        var calleeOffsets = CreateTerminalContinuationCalleeOffsets(
            plan.Body,
            expanded.Count,
            continuation.Suffix.Length);
        foreach (var instruction in plan.Body.Instructions)
        {
            if (instruction.Operation == CilOperation.Return)
            {
                foreach (var suffixInstruction in continuation.Suffix)
                {
                    expanded.Add(new(
                        suffixInstruction,
                        RemapCallerBranch: false));
                }
                continue;
            }

            var rewritten = RewriteLocalAccess(
                instruction,
                call.SourceOffset,
                argumentStart,
                calleeLocalStart,
                receiver,
                forwarded);
            expanded.Add(new(
                rewritten with
                {
                    Operand = Remap(rewritten.Operand, calleeOffsets),
                },
                RemapCallerBranch: false));
        }
    }

    private static void Inline(
        CilInstruction call,
        ManagedInliningPlan plan,
        CilInstruction? receiver,
        IReadOnlyDictionary<int, CilInstruction> forwarded,
        int? instance,
        bool pushesInstance,
        bool tryFollows,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        List<PendingInstruction> expanded)
    {
        var (argumentStart, calleeLocalStart) = AppendInlineLocals(
            plan,
            locals,
            localTypes);
        AppendArgumentStores(call, plan.Method, argumentStart, receiver is not null, forwarded, expanded);
        if (instance is { } created)
        {
            // newobj zeroes the value before its constructor runs.
            expanded.Add(new(CreateSynthetic(call, CilOperation.LoadLocalAddress, new CilOperand.Index(created)), RemapCallerBranch: false));
            expanded.Add(new(CreateSynthetic(call, CilOperation.InitializeObject, new CilOperand.TypeIdentity(plan.Method.DeclaringType)),
                RemapCallerBranch: false));
        }

        var calleeOffsets = CreateCalleeOffsets(plan.Body, expanded.Count);
        var continuation = calleeOffsets[plan.Body.Instructions[^1].NextOffset];
        foreach (var instruction in plan.Body.Instructions[..^1])
        {
            if (instruction.Operation == CilOperation.Return)
            {
                expanded.Add(new(CreateSynthetic(
                    call,
                    CilOperation.Branch,
                    new CilOperand.BranchTarget(continuation)),
                    RemapCallerBranch: false));
                continue;
            }

            var rewritten = RewriteLocalAccess(
                instruction,
                call.SourceOffset,
                argumentStart,
                calleeLocalStart,
                receiver,
                forwarded);
            expanded.Add(new(
                rewritten with
                {
                    Operand = Remap(rewritten.Operand, calleeOffsets),
                },
                RemapCallerBranch: false));
        }
        // A branch may not enter a try region from outside it, so the callee's
        // returns branch to a nop before a try that starts after the call.
        if (tryFollows)
        {
            expanded.Add(new(CreateSynthetic(call, CilOperation.Nop, new CilOperand.None()), RemapCallerBranch: false));
        }
        if (instance is { } value && pushesInstance)
        {
            expanded.Add(new(CreateSynthetic(call, CilOperation.LoadLocal, new CilOperand.Index(value)), RemapCallerBranch: false));
        }
    }

    // Instructions that neither reach a local through an address nor run other code.
    private static readonly FrozenSet<CilOperation> PlainOperations = new[]
    {
        CilOperation.Nop, CilOperation.LoadLocal, CilOperation.StoreLocal, CilOperation.LoadArgument,
        CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32, CilOperation.LoadFloat64,
        CilOperation.LoadNull, CilOperation.Duplicate, CilOperation.Pop,
        CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply,
        CilOperation.BitwiseAnd, CilOperation.BitwiseOr, CilOperation.BitwiseXor,
        CilOperation.ShiftLeft, CilOperation.ShiftRightSigned, CilOperation.ShiftRightUnsigned,
        CilOperation.Negate, CilOperation.OnesComplement,
    }.ToFrozenSet();

    // The store of a constructed value type into a local of that type, when
    // the value can be built in the local instead: a method without exception
    // regions cannot observe a constructor that stops partway, and nothing
    // between the construction and the store touches the local or runs code
    // that could reach it through an address.
    private static int? Destination(
        CilMethodBody body,
        int construction,
        CliTypeIdentity type,
        StackSlotProducers producers)
    {
        if (!body.ExceptionRegions.IsEmpty)
        {
            return null;
        }
        for (var index = construction + 1; index < body.Instructions.Length && !producers.Enters(index); index++)
        {
            var instruction = body.Instructions[index];
            if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored } &&
                producers.FindTop(index) == construction)
            {
                return body.LocalSignatureTypes[stored.Value].Equals(type) &&
                    !body.Instructions[(construction + 1)..index].Any(between =>
                        between.Operand is CilOperand.Index { Value: var local } && local == stored.Value &&
                        between.Operation is CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.StoreLocal)
                    ? index
                    : null;
            }
            if (!PlainOperations.Contains(instruction.Operation))
            {
                return null;
            }
        }
        return null;
    }

    // A forwarded argument must not be replaced or have its own address taken.
    private static bool WritesArgument(CilMethodBody body, int argument) => body.Instructions.Any(instruction => instruction is
    { Operation: CilOperation.StoreArgument or CilOperation.LoadArgumentAddress, Operand: CilOperand.Index index } &&
        index.Value == argument);

    // A forwarded value argument may also have its address taken for a field
    // read: the read sees the caller's slot, which holds the same value.
    private static bool ReplacesArgument(CilMethodBody body, int argument) => body.Instructions.Where((instruction, index) =>
        instruction.Operation == CilOperation.StoreArgument ||
        instruction.Operation == CilOperation.LoadArgumentAddress && !ReadsFieldOnly(body, index)).Any(instruction =>
        ((CilOperand.Index)instruction.Operand).Value == argument);

    // The field load consumes the address at once, so the slot neither changes
    // nor stays reachable through it.
    private static bool ReadsFieldOnly(CilMethodBody body, int index) =>
        index + 1 < body.Instructions.Length && body.Instructions[index + 1].Operation == CilOperation.LoadField;

    // Arguments the body reads from the caller's local or argument instead of
    // a spilled copy, as it reads a receiver address. The body cannot name a
    // caller slot, so the slot keeps its value while the body runs when no
    // address of it exists and nothing between the load and the call stores
    // it. The slot's type must be the parameter's, so the body sees the value
    // it would have stored, and a value constructed in place is zeroed first.
    private static ImmutableDictionary<int, CilInstruction> ForwardedArguments(
        CilMethodBody body,
        int callerIndex,
        MethodInstanceModel caller,
        ManagedInliningPlan plan,
        bool receiverBound,
        int? instance,
        HashSet<(bool IsArgument, int Index)> exposed,
        StackSlotProducers producers,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks)
    {
        var call = body.Instructions[callerIndex];
        var parameters = GetArgumentTypes(plan.Method);
        // newobj's instance is not on the stack.
        var first = call.Operation == CilOperation.NewObject ? 1 : 0;
        if (!instructionEntryStacks.TryGetValue(call.Offset, out var stack) ||
            stack.Length < parameters.Length - first)
        {
            return ImmutableDictionary<int, CilInstruction>.Empty;
        }
        var bottom = stack.Length - (parameters.Length - first);
        var callerArguments = GetArgumentTypes(caller);
        var forwarded = ImmutableDictionary.CreateBuilder<int, CilInstruction>();
        for (var parameter = receiverBound ? Math.Max(first, 1) : first; parameter < parameters.Length; parameter++)
        {
            var source = producers.Find(callerIndex, bottom + parameter - first);
            // A local's or argument's address never changes, so a byref
            // parameter the body neither replaces nor takes the address of is
            // the caller's address, as a receiver is.
            if (source is { } addressProducer &&
                body.Instructions[addressProducer] is
                {
                    Operation: CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress,
                    Operand: CilOperand.Index addressed,
                } address &&
                !WritesArgument(plan.Body, parameter) &&
                Unmodified(parameters[parameter]) is { Shape: CliTypeShape.ManagedByReference, ElementType: { } element } &&
                element.Equals(address.Operation == CilOperation.LoadArgumentAddress
                    ? callerArguments[addressed.Value]
                    : body.LocalSignatureTypes.ElementAtOrDefault(addressed.Value)))
            {
                forwarded.Add(parameter, address);
                continue;
            }
            if (ReplacesArgument(plan.Body, parameter) ||
                source is not { } producer ||
                body.Instructions[producer] is not
                { Operation: CilOperation.LoadLocal or CilOperation.LoadArgument, Operand: CilOperand.Index slot } load)
            {
                continue;
            }
            var isArgument = load.Operation == CilOperation.LoadArgument;
            var stored = isArgument ? CilOperation.StoreArgument : CilOperation.StoreLocal;
            if ((isArgument ? callerArguments[slot.Value] : body.LocalSignatureTypes[slot.Value]).Equals(parameters[parameter]) &&
                !exposed.Contains((isArgument, slot.Value)) &&
                !(!isArgument && instance == slot.Value) &&
                !body.Instructions[(producer + 1)..callerIndex].Any(between =>
                    between.Operation == stored && between.Operand is CilOperand.Index { Value: var target } &&
                    target == slot.Value))
            {
                forwarded.Add(parameter, load);
            }
        }
        return forwarded.ToImmutable();
    }

    // An in parameter's byref carries a required modifier.
    private static CliTypeIdentity Unmodified(CliTypeIdentity type) =>
        type is { Shape: CliTypeShape.Modified, ElementType: { } inner } ? Unmodified(inner) : type;

    private static (int ArgumentStart, int CalleeLocalStart) AppendInlineLocals(
        ManagedInliningPlan plan,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes)
    {
        var argumentTypes = GetArgumentTypes(plan.Method);
        var argumentStart = locals.Count;
        foreach (var parameter in argumentTypes)
        {
            locals.Add(parameter.StackKind);
            localTypes.Add(parameter);
        }

        var calleeLocalStart = locals.Count;
        locals.AddRange(plan.Body.Locals);
        localTypes.AddRange(plan.Body.LocalSignatureTypes);
        return (argumentStart, calleeLocalStart);
    }

    private static void AppendArgumentStores(
        CilInstruction call,
        MethodInstanceModel method,
        int argumentStart,
        bool forwardedReceiver,
        IReadOnlyDictionary<int, CilInstruction> forwarded,
        List<PendingInstruction> expanded)
    {
        var argumentTypes = GetArgumentTypes(method);
        for (var argument = argumentTypes.Length - 1;
             argument >= (forwardedReceiver ? 1 : 0);
             argument--)
        {
            if (forwarded.ContainsKey(argument)) continue;
            expanded.Add(new(CreateSynthetic(
                call,
                CilOperation.StoreLocal,
                new CilOperand.Index(argumentStart + argument)),
                RemapCallerBranch: false));
        }
    }

    private static ImmutableArray<CliTypeIdentity> GetArgumentTypes(
        MethodInstanceModel method) => method.Definition.IsStatic
        ? method.Signature.ParameterSignatureTypes
        : method.Signature.ParameterSignatureTypes.Insert(
            0,
            method.DeclaringType.IsValueType
                ? CliTypeIdentity.ManagedByReference(method.DeclaringType)
                : method.DeclaringType);

    private static Dictionary<int, int> CreateCalleeOffsets(
        CilMethodBody body,
        int firstOutputIndex)
    {
        var offsets = new Dictionary<int, int>(body.Instructions.Length + 1);
        var outputIndex = firstOutputIndex;
        foreach (var instruction in body.Instructions[..^1])
        {
            offsets.Add(instruction.Offset, outputIndex++);
        }

        var terminal = body.Instructions[^1];
        offsets.Add(terminal.Offset, outputIndex);
        offsets.Add(terminal.NextOffset, outputIndex);
        return offsets;
    }

    private static Dictionary<int, int> CreateTerminalContinuationCalleeOffsets(
        CilMethodBody body,
        int firstOutputIndex,
        int continuationLength)
    {
        var offsets = new Dictionary<int, int>(body.Instructions.Length + 1);
        var outputIndex = firstOutputIndex;
        foreach (var instruction in body.Instructions)
        {
            offsets.Add(instruction.Offset, outputIndex);
            outputIndex += instruction.Operation == CilOperation.Return
                ? continuationLength
                : 1;
        }

        offsets.Add(body.Instructions[^1].NextOffset, outputIndex);
        return offsets;
    }

    private static CilInstruction RewriteLocalAccess(
        CilInstruction instruction,
        int sourceOffset,
        int argumentStart,
        int calleeLocalStart,
        CilInstruction? receiver,
        IReadOnlyDictionary<int, CilInstruction> forwarded)
    {
        var rewritten = instruction.Operation switch
        {
            CilOperation.LoadArgument when receiver is not null &&
                instruction.Operand is CilOperand.Index { Value: 0 } => receiver,
            CilOperation.LoadArgument when instruction.Operand is CilOperand.Index argument &&
                forwarded.TryGetValue(argument.Value, out var load) => load,
            CilOperation.LoadArgument => Rewrite(
                instruction,
                CilOperation.LoadLocal,
                argumentStart),
            // A forwarded value is read in place: its field reads use the caller slot's address.
            CilOperation.LoadArgumentAddress when instruction.Operand is CilOperand.Index argument &&
                forwarded.TryGetValue(argument.Value, out var source) => source with
                {
                    Operation = source.Operation == CilOperation.LoadArgument
                        ? CilOperation.LoadArgumentAddress
                        : CilOperation.LoadLocalAddress,
                },
            CilOperation.LoadArgumentAddress => Rewrite(
                instruction,
                CilOperation.LoadLocalAddress,
                argumentStart),
            CilOperation.StoreArgument => Rewrite(
                instruction,
                CilOperation.StoreLocal,
                argumentStart),
            CilOperation.LoadLocal or
            CilOperation.LoadLocalAddress or
            CilOperation.StoreLocal => Rewrite(
                instruction,
                instruction.Operation,
                calleeLocalStart),
            _ => instruction,
        };
        return rewritten with
        {
            Offset = -1,
            NextOffset = -1,
            OriginalOffset = sourceOffset,
        };
    }

    private static CilInstruction Rewrite(
        CilInstruction instruction,
        CilOperation operation,
        int start)
    {
        if (instruction.Operand is not CilOperand.Index index)
        {
            throw new InvalidOperationException(
                "Inline argument and local operations require index operands.");
        }

        return instruction with
        {
            Operation = operation,
            Operand = new CilOperand.Index(start + index.Value),
        };
    }

    private static CilInstruction CreateSynthetic(
        CilInstruction call,
        CilOperation operation,
        CilOperand operand) => new(
            -1,
            -1,
            operation,
            operand)
        {
            OriginalOffset = call.SourceOffset,
        };

    private static CilOperand Remap(
        CilOperand operand,
        IReadOnlyDictionary<int, int> offsets) => operand switch
        {
            CilOperand.BranchTarget target => new CilOperand.BranchTarget(
                ResolveOffset(target.Offset, offsets)),
            CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets(
                [.. targets.Offsets.Select(offset => ResolveOffset(offset, offsets))]),
            _ => operand,
        };

    private static int ResolveOffset(
        int offset,
        IReadOnlyDictionary<int, int> offsets) =>
        offsets.TryGetValue(offset, out var mapped)
            ? mapped
            : throw new InvalidOperationException(
                "Inline branch target does not identify an instruction boundary.");

    private sealed record PendingInstruction(
        CilInstruction Instruction,
        bool RemapCallerBranch);

    // Each backward branch closes a loop from its target to the branch itself.
    private static (int Start, int End)[] LoopSpans(CilMethodBody body) =>
        body.Instructions
            .SelectMany(static instruction => instruction.Operand switch
            {
                CilOperand.BranchTarget target => [target.Offset],
                CilOperand.SwitchTargets targets => targets.Offsets,
                _ => ImmutableArray<int>.Empty,
            },
            static (instruction, target) => (Start: target, End: instruction.Offset))
            .Where(static loop => loop.Start <= loop.End)
            .ToArray();
}
