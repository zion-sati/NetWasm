// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.CompositeFormats;

// string.Format(string, object...) and its overload with a provider read their format at run time and formats each argument through ISpanFormattable
// or IFormattable with whatever item format the hole names, so a program that reaches it carries the parser of
// every item format of every number type it boxes anywhere: most of a program that only encodes UTF-8, whose error
// messages are SR.Format calls. Where the format is a valid string constant that indexes only the arguments given,
// the call becomes TypedCompositeFormat.Format with the arguments as they were before boxing (the box goes) and a hole
// type for each, which writes the same text through CompositeFormatBuilder: a program then carries only its
// argument types' own formatting, as an interpolated string does, and the parser of item formats only for an argument
// a hole names one for, unless it is a primitive integer whose item formats are all hexadecimal, which HexHole writes
// without it. A reference whose static type nothing IFormattable can be is written by its ToString(), which
// is what string.Format writes for it with any item format; any other keeps string.Format's handling of an object.
// The format and the boxes are found where every path to the call agrees on them, so an argument written with ?. or
// ?? after them does not hide them; a box goes only where the call alone reads what it pushed. The call is passed the
// format encoded as CompositeFormatParser writes it, which CoreLib reads without parsing, so a format the call does not
// alone read, or with an alignment past the encoding's, keeps string.Format. Two forward analyses of the body, each
// linear in it for the few values a stack holds, serve every call in it.
internal sealed class ConstantCompositeFormatRewriter(
    ICompositeFormatCallResolver formatCalls,
    IToStringOnlyTypeClassifier toStringOnly,
    INullableTypeResolver nullables,
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IStackSlotOriginAnalyzer origins,
    IStaticReferenceTypeAnalyzer staticTypes) : IMethodRewriteRule
{
    private const string HelperType = "System.Text.TypedCompositeFormat";
    private const string FormattedHole = "System.Text.FormattedHole`1";
    private const string ItemFormattedHole = "System.Text.ItemFormattedHole`1";
    private const string ToStringHole = "System.Text.ToStringHole`1";
    private const string HexHole = "System.Text.HexHole`1";

    // The most arguments TypedCompositeFormat.Format takes.
    private const int MaximumArguments = 8;

    // The integers HexHole writes, as InterpolatedHexHoleRewriter's helper does.
    internal static readonly FrozenSet<string> PrimitiveIntegers = new[]
    {
        "primitive:u1", "primitive:i1", "primitive:i2", "primitive:u2", "primitive:i4", "primitive:u4",
        "primitive:i8", "primitive:u8", "primitive:nativeint", "primitive:nativeuint",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
    private static readonly CliTypeIdentity String =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

    private readonly ConcurrentDictionary<CompositeFormatCall, MethodDefinitionModel> _helpers = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(instruction => formatCalls.Resolve(instruction) is not null))
        {
            return body;
        }
        var validated = stacks.Validate(graphs.Build(body));
        var producers = origins.Analyze(validated);
        ImmutableDictionary<int, ImmutableArray<CliTypeIdentity?>>? declared = null;
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        // Each element of a params array no longer stored leaves a value more on the stack for the call.
        var maximumStack = body.MaxStack;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (formatCalls.Resolve(instruction) is not { } call ||
                !validated.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack))
            {
                continue;
            }
            // Where each argument is read: by the call, or for params by its store into the array the call is passed.
            var removed = new List<int>();
            (int Offset, int Slot, int Reader)[] sources;
            int formatSlot;
            if (call.Params)
            {
                if (stack.Length < 2 ||
                    ArrayElements(body, validated, producers, index, stack.Length - 1, removed) is not { } elements)
                {
                    continue;
                }
                sources = elements;
                formatSlot = stack.Length - 2;
                maximumStack = Math.Max(maximumStack, body.MaxStack + elements.Length);
            }
            else
            {
                if (stack.Length < call.Arguments + 1)
                {
                    continue;
                }
                formatSlot = stack.Length - call.Arguments - 1;
                sources = [.. Enumerable.Range(0, call.Arguments).Select(argument =>
                    (instruction.Offset, formatSlot + 1 + argument, index))];
            }
            var count = sources.Length;
            if (producers.Producer(instruction.Offset, formatSlot) is not { } formatProducer ||
                body.Instructions[formatProducer] is not
                {
                    Operation: CilOperation.LoadString,
                    Operand: CilOperand.UserString { Value: var format },
                } ||
                !CompositeFormatParser.TryParse(format, out var indexed, out var itemFormatted, out var otherThanHex, out var encoded) ||
                indexed > count ||
                encoded is null ||
                !producers.IsOnlyReadBy(formatProducer, index))
            {
                continue;
            }

            var arguments = new CliTypeIdentity[count];
            var holes = new CliTypeIdentity[count];
            var boxes = new List<int>(count);
            for (var argument = 0; argument < count; argument++)
            {
                var (offset, slot, reader) = sources[argument];
                var bit = 1u << argument;
                var formatted = (itemFormatted & bit) == 0 ? FormattedHole : ItemFormattedHole;
                if (producers.Producer(offset, slot) is { } box &&
                    Unboxed(body.Instructions[box]) is { } value &&
                    producers.IsOnlyReadBy(box, reader))
                {
                    arguments[argument] = value;
                    holes[argument] = Hole(
                        (itemFormatted & bit) != 0 && (otherThanHex & bit) == 0 && PrimitiveIntegers.Contains(value.CanonicalName)
                            ? HexHole
                            : formatted,
                        value);
                    boxes.Add(box);
                    continue;
                }
                // A null literal is a string's null to the hole, which writes nothing for it.
                var reference = producers.Producer(offset, slot) is { } literal &&
                    body.Instructions[literal].Operation == CilOperation.LoadNull
                    ? String
                    : Declared(declared ??= staticTypes.Analyze(validated), offset, slot);
                if (reference is { IsValueType: false } && toStringOnly.IsToStringOnly(reference))
                {
                    arguments[argument] = reference;
                    holes[argument] = Hole(ToStringHole, reference);
                }
                else
                {
                    arguments[argument] = Object;
                    holes[argument] = Hole(formatted, Object);
                }
            }

            rewritten ??= body.Instructions.ToBuilder();
            rewritten[formatProducer] = body.Instructions[formatProducer] with { Operand = new CilOperand.UserString(encoded) };
            foreach (var box in boxes.Concat(removed))
            {
                rewritten[box] = body.Instructions[box] with
                {
                    Operation = CilOperation.Nop,
                    Operand = new CilOperand.None(),
                };
            }
            rewritten[index] = instruction with
            {
                Operand = new CilOperand.MethodInstance(Helper(new CompositeFormatCall(count, call.Provider), [.. arguments, .. holes])),
            };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable(), MaxStack = maximumStack };
    }

    // The elements of the object array a params call is passed, as C# builds it: a length, newarr object, then for each
    // element in order dup, its index, its value and stelem.ref, the last dup's array being the call's argument. Each
    // value is read where it is stored. The array and its copies are read only there, so without the length, newarr,
    // dups, indices and stores each value stays on the stack in order for the call. Null for any other array, or more
    // elements than TypedCompositeFormat takes.
    private static (int Offset, int Slot, int Reader)[]? ArrayElements(
        CilMethodBody body, ValidatedControlFlowGraph validated, StackSlotOrigins producers, int call, int arraySlot,
        List<int> removed)
    {
        var instructions = body.Instructions;
        var dups = new List<int>();
        var current = producers.Producer(instructions[call].Offset, arraySlot);
        while (current is { } dup && instructions[dup].Operation == CilOperation.Duplicate)
        {
            dups.Add(dup);
            current = producers.Producer(instructions[dup].Offset, Top(dup));
        }
        dups.Reverse();
        if (current is not { } array ||
            instructions[array] is not
            {
                Operation: CilOperation.NewArray,
                Operand: CilOperand.TypeIdentity { Value.CanonicalName: "primitive:object" },
            } ||
            producers.Producer(instructions[array].Offset, Top(array)) is not { } length ||
            instructions[length] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var count } } ||
            count != dups.Count || count is < 1 or > MaximumArguments ||
            !producers.ReadersOf(length).SetEquals([array]) ||
            !producers.ReadersOf(array).SetEquals([dups[0]]))
        {
            return null;
        }
        var elements = new (int Offset, int Slot, int Reader)[count];
        for (var element = 0; element < count; element++)
        {
            var next = element + 1 < count ? dups[element + 1] : call;
            var readers = producers.ReadersOf(dups[element]);
            if (readers.Count != 2 || !readers.Contains(next))
            {
                return null;
            }
            var store = readers.First(reader => reader != next);
            var position = dups[element] + 1;
            if (instructions[store].Operation != CilOperation.StoreArrayElementReference ||
                store < position || store > next ||
                instructions[position] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var stored } } ||
                stored != element ||
                !producers.ReadersOf(position).SetEquals([store]))
            {
                return null;
            }
            elements[element] = (instructions[store].Offset, Top(store), store);
            removed.AddRange([dups[element], position, store]);
        }
        removed.AddRange([length, array]);
        return elements;

        int Top(int instruction) => validated.InstructionEntryStacks[instructions[instruction].Offset].Length - 1;
    }

    // The value a box of a closed value type other than Nullable<T> boxes, which its hole takes as it is. A boxed
    // Nullable<T> is its value or null, as an object hole formats it.
    private CliTypeIdentity? Unboxed(CilInstruction instruction) =>
        instruction is { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var type } } &&
        type.IsValueType && !type.ContainsGenericParameters && nullables.Resolve(type) is null
            ? type
            : null;

    private static CliTypeIdentity? Declared(
        ImmutableDictionary<int, ImmutableArray<CliTypeIdentity?>> declared, int offset, int slot) =>
        declared.TryGetValue(offset, out var stack) && slot < stack.Length ? stack[slot] : null;

    private CliTypeIdentity Hole(string hole, CliTypeIdentity type) =>
        CliTypeIdentity.GenericInstantiation(CliTypeIdentity.FromDefinition(types.FindType(hole)), [type]);

    // TypedCompositeFormat.Format for the argument count, with a provider first or none, with each argument's type
    // and then each hole type.
    private MethodInstanceModel Helper(CompositeFormatCall call, ImmutableArray<CliTypeIdentity> typeArguments)
    {
        var helper = _helpers.GetOrAdd(call, call => types.FindType(HelperType).Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == "Format" && method.GenericArity == 2 * call.Arguments &&
                (method.Signature.ParameterSignatureTypes[0].FullName == "System.IFormatProvider") == call.Provider));
        return methodInstances.ResolveMethodInstance(
            helper.Key.Assembly,
            helper.Key.MetadataToken,
            symbols.Format(helper),
            0,
            new CliGenericContext([], typeArguments));
    }
}
