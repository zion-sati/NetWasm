// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// How a method receives its struct arguments that the convention passes by their fields (StructParameters): each
// argument's first Wasm parameter; those whose fields the entry stores into a frame copy (Copied); and those read in
// place from their parameters (InPlace), with the field loads that read a parameter, the whole loads a call takes as
// an argument (Forwarded, pushing the parameters) or a ret returns (Returned), and the arguments used only in the
// entry block before anything that could collect (Confined), which are no root.
internal sealed record FieldArgumentPlan(
    ImmutableArray<int> ArgumentParameters,
    ImmutableDictionary<int, FieldArgument> Copied,
    ImmutableDictionary<int, FieldArgument> InPlace,
    ImmutableHashSet<int> FieldAddresses,
    ImmutableDictionary<int, (int Parameter, CliValueKind Kind)> FieldLoads,
    ImmutableDictionary<(int Call, int Argument), int> Forwarded,
    ImmutableHashSet<int> ForwardedLoads,
    ImmutableDictionary<int, int> Returned,
    ImmutableHashSet<int> Confined);

internal interface IFieldArgumentPlanner
{
    FieldArgumentPlan Plan(StructuredMethod method, MethodDefinitionModel definition, MethodSignatureModel signature,
        StructResultShape? returnShape);
}

internal sealed class FieldArgumentPlanner(IStructResultClassifier classifier, IInstanceFieldLayoutProvider layouts,
    IStructConventionCallClassifier calls) : IFieldArgumentPlanner
{
    private readonly IStructConventionCallClassifier _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly IStructResultClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly IInstanceFieldLayoutProvider _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));

    public FieldArgumentPlan Plan(StructuredMethod method, MethodDefinitionModel definition, MethodSignatureModel signature,
        StructResultShape? returnShape)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(signature);
        // The struct arguments passed by their fields, and each argument's first Wasm parameter.
        var argumentParameters = ImmutableArray.CreateBuilder<int>();
        var fieldArguments = ImmutableDictionary.CreateBuilder<int, FieldArgument>();
        var fieldArgumentsInPlace = new Dictionary<int, FieldArgument>();
        {
            var isStatic = definition.IsStatic;
            var parameterTypes = signature.ParameterSignatureTypes;
            var next = signature.ReturnSignatureType.StackKind == CliValueKind.ValueType &&
                _classifier.Classify(signature.ReturnSignatureType) is null ? 1 : 0;
            var count = parameterTypes.Length + (isStatic ? 0 : 1);
            for (var argument = 0; argument < count; argument++)
            {
                var type = argument < (isStatic ? 0 : 1) ? null : parameterTypes[argument - (isStatic ? 0 : 1)];
                if (type is { StackKind: CliValueKind.ValueType, ContainsGenericParameters: false } &&
                    _classifier.Classify(type) is { } shape)
                {
                    fieldArguments[argument] = new(shape, next);
                    argumentParameters.Add(-1);
                    next += shape.Results.Length;
                }
                else
                {
                    argumentParameters.Add(next++);
                }
            }
        }
        // A field argument whose every use is an address a field load takes right away (as the scalar
        // replacer's entry copy of a split argument is), or a whole load a call taking it by its fields takes as an
        // argument, is read from its parameters: no frame copy. Its reference fields are published from the
        // parameters that hold them; one used only in the entry block before anything that could collect is no root.
        var parameterFieldAddresses = ImmutableHashSet.CreateBuilder<int>();
        var parameterFieldLoads = ImmutableDictionary.CreateBuilder<int, (int, CliValueKind)>();
        var forwardedArguments = ImmutableDictionary.CreateBuilder<(int, int), int>();
        var returnedArguments = ImmutableDictionary.CreateBuilder<int, int>();
        var forwardedLoads = ImmutableHashSet.CreateBuilder<int>();
        var confinedArguments = new HashSet<int>();
        var hasFilters = method.ExceptionGroups.Values.Any(group => group.Clauses.Any(clause => clause.FilterOffset is not null));
        {
            foreach (var (argument, fieldArgument) in fieldArguments.ToArray())
            {
                var addresses = new List<int>();
                var loads = new List<(int, int, CliValueKind)>();
                var forwards = new List<((int, int) Call, int Load)>();
                var returns = new List<(int Return, int Load)>();
                var only = true;
                var confined = true;
                foreach (var block in method.Blocks.Values)
                {
                    var instructions = block.Instructions;
                    var entry = block.Id == method.EntryBlock;
                    var collectable = false;
                    for (var index = 0; index < instructions.Length && only; index++)
                    {
                        var instruction = instructions[index];
                        if (!CallResultLanes.KeepsLanes(instruction.Operation)) collectable = true;
                        if (instruction.Operation is not (CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or
                                CilOperation.StoreArgument) || CilOperandReader.GetIndex(instruction) != argument)
                        {
                            continue;
                        }
                        if (!entry || collectable) confined = false;
                        if (instruction.Operation == CilOperation.LoadArgument &&
                            ForwardingCall(instructions, index) is { } forwarding)
                        {
                            forwards.Add((forwarding, instruction.Offset));
                            continue;
                        }
                        // Returned whole by a method that returns its type in results: the return pushes the parameters.
                        if (instruction.Operation == CilOperation.LoadArgument && returnShape is not null &&
                            returnShape.Type.CanonicalName == fieldArgument.Shape.Type.CanonicalName)
                        {
                            var next = index + 1;
                            while (next < instructions.Length && instructions[next].Operation == CilOperation.Nop) next++;
                            // The ret is the block's exit, after its instructions.
                            if (next == instructions.Length &&
                                block.Exit is StructuredTerminalExit { Instruction: { Operation: CilOperation.Return } exit })
                            {
                                returns.Add((exit.Offset, instruction.Offset));
                                continue;
                            }
                        }
                        // The address goes to one ldfld, or, as the scalar replacer's entry copy of a split argument
                        // reads it, through a chain of `dup; ldfld f; stloc` ending in an ldfld (nops between).
                        if (instruction.Operation != CilOperation.LoadArgumentAddress)
                        {
                            only = false;
                            break;
                        }
                        var at = index + 1;
                        while (at < instructions.Length && instructions[at].Operation == CilOperation.Nop) at++;
                        var chain = new List<int>();
                        while (at + 2 < instructions.Length && instructions[at].Operation == CilOperation.Duplicate &&
                            instructions[at + 1].Operation == CilOperation.LoadField &&
                            instructions[at + 2].Operation == CilOperation.StoreLocal)
                        {
                            chain.Add(at + 1);
                            at += 3;
                        }
                        if (at >= instructions.Length || instructions[at].Operation != CilOperation.LoadField)
                        {
                            only = false;
                            break;
                        }
                        chain.Add(at);
                        foreach (var load in chain)
                        {
                            if (instructions[load].Operand is not CilOperand.FieldInstance chained)
                            {
                                only = false;
                                break;
                            }
                            var lane = fieldArgument.Shape.LaneOf(_layouts.GetFieldLayout(chained.Value).Offset, chained.Value.FieldType);
                            if (lane < 0)
                            {
                                only = false;
                                break;
                            }
                            loads.Add((instructions[load].Offset, fieldArgument.FirstParameter + lane, fieldArgument.Shape.Fields[lane].Kind));
                        }
                        if (!only) break;
                        addresses.Add(instruction.Offset);
                        index = at;
                    }
                    if (!only) break;
                }
                // A filter's funclet reads its captures from the parent's frame, which an argument in place is not in.
                if (!only || !confined && hasFilters) continue;
                if (confined) confinedArguments.Add(argument);
                foreach (var (call, load) in forwards)
                {
                    forwardedArguments[call] = argument;
                    forwardedLoads.Add(load);
                }
                foreach (var (ret, load) in returns)
                {
                    returnedArguments[ret] = argument;
                    forwardedLoads.Add(load);
                }
                parameterFieldAddresses.UnionWith(addresses);
                foreach (var (at, parameter, kind) in loads) parameterFieldLoads[at] = (parameter, kind);
                // Read in place: no frame copy, so not a field argument for the frame.
                fieldArguments.Remove(argument);
                fieldArgumentsInPlace.Add(argument, fieldArgument);
            }
        }
        return new([.. argumentParameters], fieldArguments.ToImmutable(), fieldArgumentsInPlace.ToImmutableDictionary(),
            parameterFieldAddresses.ToImmutable(), parameterFieldLoads.ToImmutable(), forwardedArguments.ToImmutable(),
            forwardedLoads.ToImmutable(), returnedArguments.ToImmutable(), [.. confinedArguments]);
    }

    // The call, and the argument position among the values it takes (the receiver first), of a managed call
    // that takes the whole load at the index as a struct argument passed by its fields, through instructions that
    // neither branch, call nor collect; or null.
    private (int Call, int Position)? ForwardingCall(ImmutableArray<CilInstruction> instructions, int index)
    {
        var above = 0;
        for (var next = index + 1; next < instructions.Length; next++)
        {
            var instruction = instructions[next];
            if (instruction.Operation is CilOperation.Call or CilOperation.CallVirtual)
            {
                if (instruction.Operand is not CilOperand.MethodInstance { Value: var called } ||
                    !_calls.CallsBody(called))
                {
                    return null;
                }
                var receiver = called.Definition.IsStatic ? 0 : 1;
                var pops = called.Signature.ParameterTypes.Length + receiver;
                if (pops <= above) return null;
                var position = pops - 1 - above;
                return position >= receiver &&
                    StructParameters.Parameter(called.Signature, called.Definition.IsStatic, position) is { } parameter &&
                    StructParameters.Shape(_classifier, parameter) is not null
                    ? (instruction.Offset, position)
                    : null;
            }
            if (!CallResultLanes.KeepsLanes(instruction.Operation) || CilStackEffects.Of(instruction) is not { } effect ||
                effect.Pops > above)
            {
                return null;
            }
            above += effect.Pushes - effect.Pops;
        }
        return null;
    }
}
