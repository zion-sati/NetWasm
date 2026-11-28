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
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.CompositeFormats;

// An interpolated hole with an item format, such as {b:X2}, calls the handler's AppendFormatted<T>(value, format),
// which passes the format to the value's TryFormat: a program that reaches one carries Number's parser of every item
// format of the type, with the format read at run time. Where the value is a primitive integer, the format a
// constant hexadecimal one and the handler a DefaultInterpolatedStringHandler or a StringBuilder's handler built
// without a custom formatter, the call becomes InterpolatedHexHoles' method for that handler, which writes the same
// digits through the handler's span overloads without the parser (HexadecimalDigits, as HexHole writes them).
//
// A handler has a custom formatter only from a provider its constructor took, so the rewrite needs every handler the
// local holds to be built without one: each address of the local is read only by calls to the handler's own members,
// a constructor without a provider or with null or the invariant culture (whose GetFormat gives no formatter), or to
// the members of StringBuilder or string that take the handler by reference to finish it; the local is never stored
// to as a whole. Any other reader, a dup, a store or a path that meets the address with another value leaves every
// hole on that local as it was. One forward analysis of the body serves every call in it, and each address is
// checked once, so the rule is linear in the body.
internal sealed class InterpolatedHexHoleRewriter(
    ICalledMethodResolver calledMethods,
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IStackSlotOriginAnalyzer origins) : IMethodRewriteRule
{
    private const string HelperType = "System.Text.InterpolatedHexHoles";
    private const string DefaultHandler = "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler";
    private const string BuilderHandler = "System.Text.StringBuilder+AppendInterpolatedStringHandler";

    // Each handler's helper without and with an alignment.
    private static readonly FrozenDictionary<string, (string Plain, string Aligned)> Handlers =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [DefaultHandler] = ("AppendDefault", "AppendDefaultAligned"),
            [BuilderHandler] = ("AppendBuilder", "AppendBuilderAligned"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, MethodDefinitionModel> _helpers = new(StringComparer.Ordinal);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(instruction => Hole(instruction) is not null))
        {
            return body;
        }
        var validated = stacks.Validate(graphs.Build(body));
        var producers = origins.Analyze(validated);
        Dictionary<int, bool>? plainLocals = null;
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (Hole(instruction) is not { } hole ||
                !validated.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack) ||
                stack.Length < hole.Parameters + 1 ||
                producers.Producer(instruction.Offset, stack.Length - 1) is not { } formatProducer ||
                body.Instructions[formatProducer] is not
                {
                    Operation: CilOperation.LoadString,
                    Operand: CilOperand.UserString { Value: var format },
                } ||
                !CompositeFormatParser.IsHexadecimal(format) ||
                producers.Producer(instruction.Offset, stack.Length - hole.Parameters - 1) is not { } receiver ||
                body.Instructions[receiver] is not
                {
                    Operation: CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index { Value: var local },
                } ||
                !(plainLocals ??= PlainLocals(body, validated, producers))[local])
            {
                continue;
            }
            var names = Handlers[hole.Method.DeclaringType.FullName!];
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index] = instruction with
            {
                Operation = CilOperation.Call,
                Operand = new CilOperand.MethodInstance(
                    Helper(hole.Parameters == 3 ? names.Aligned : names.Plain, hole.Method.MethodArguments[0])),
            };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    // The handler's AppendFormatted<T>(T, string) or AppendFormatted<T>(T, int, string) for a primitive integer T,
    // with how many values it takes after the handler; null for any other instruction. A generic method's instance is
    // the call's operand, so every other call is passed over without resolving it.
    private static (MethodInstanceModel Method, int Parameters)? Hole(CilInstruction instruction)
    {
        if (instruction is not { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } } ||
            method.Definition is not { Name: "AppendFormatted", GenericArity: 1, IsStatic: false } definition ||
            method.DeclaringType.FullName is not { } handler || !Handlers.ContainsKey(handler) ||
            method.MethodArguments.Length != 1 ||
            !ConstantCompositeFormatRewriter.PrimitiveIntegers.Contains(method.MethodArguments[0].CanonicalName))
        {
            return null;
        }
        var parameters = definition.Signature.ParameterSignatureTypes;
        return parameters.Length switch
        {
            2 when IsString(parameters[1]) => (method, 2),
            3 when parameters[1].CanonicalName == "primitive:i4" && IsString(parameters[2]) => (method, 3),
            _ => null,
        };
    }

    // For each local whose address the body takes: whether every handler it holds is built without a custom
    // formatter, as the class comment sets out.
    private Dictionary<int, bool> PlainLocals(
        CilMethodBody body, ValidatedControlFlowGraph validated, StackSlotOrigins producers)
    {
        var providers = ProviderLocals(body, validated, producers);
        var plain = new Dictionary<int, bool>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            switch (body.Instructions[index])
            {
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } }:
                    plain[stored] = false;
                    break;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var addressed } }
                    when plain.GetValueOrDefault(addressed, true):
                    plain[addressed] = ReadOnlyByPlainHandlerCalls(body, validated, producers, providers, index);
                    break;
            }
        }
        return plain;
    }

    // For each local the body stores to: whether every value stored is null or the invariant culture and its address
    // is never taken, so that loading it gives one of them. C# keeps a provider in such a local when it passes it both
    // to the handler's constructor and to the method the handler is for, as string.Create(provider, $"...") does.
    private Dictionary<int, bool> ProviderLocals(
        CilMethodBody body, ValidatedControlFlowGraph validated, StackSlotOrigins producers)
    {
        var providers = new Dictionary<int, bool>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            switch (instruction)
            {
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } }:
                    providers[stored] = providers.GetValueOrDefault(stored, true) &&
                        validated.InstructionEntryStacks.TryGetValue(instruction.Offset, out var stack) &&
                        producers.Producer(instruction.Offset, stack.Length - 1) is { } value &&
                        HasNoCustomFormatter(body.Instructions[value]);
                    break;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var addressed } }:
                    providers[addressed] = false;
                    break;
            }
        }
        return providers;
    }

    // Whether the address the instruction pushed is read, on every path, only by the calls a handler without a custom
    // formatter is built, written and finished with.
    private bool ReadOnlyByPlainHandlerCalls(
        CilMethodBody body, ValidatedControlFlowGraph validated, StackSlotOrigins producers,
        Dictionary<int, bool> providers, int address)
    {
        var readers = producers.ReadersOf(address);
        if (readers.IsEmpty)
        {
            return false;
        }
        foreach (var reader in readers)
        {
            var instruction = body.Instructions[reader];
            if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual) ||
                calledMethods.Resolve(instruction) is not { } method)
            {
                return false;
            }
            if (method.DeclaringType.FullName is { } declaring && Handlers.ContainsKey(declaring))
            {
                if (method.Definition.Name == ".ctor" && !WithoutCustomFormatter(body, validated, producers, providers, reader, method))
                {
                    return false;
                }
            }
            else if (method.DeclaringType.CanonicalName != "primitive:string" &&
                method.DeclaringType.FullName != "System.Text.StringBuilder")
            {
                return false;
            }
        }
        return true;
    }

    // A handler constructor with no provider, or with a provider that is null or the invariant culture, directly or
    // through a local that holds only those.
    private bool WithoutCustomFormatter(
        CilMethodBody body, ValidatedControlFlowGraph validated, StackSlotOrigins producers,
        Dictionary<int, bool> providers, int constructor, MethodInstanceModel method)
    {
        var parameters = method.Definition.Signature.ParameterSignatureTypes;
        var provider = -1;
        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].FullName == "System.IFormatProvider")
            {
                provider = index;
            }
        }
        if (provider < 0)
        {
            return true;
        }
        var offset = body.Instructions[constructor].Offset;
        if (!validated.InstructionEntryStacks.TryGetValue(offset, out var stack) ||
            producers.Producer(offset, stack.Length - parameters.Length + provider) is not { } value)
        {
            return false;
        }
        return body.Instructions[value] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
            ? providers.GetValueOrDefault(local)
            : HasNoCustomFormatter(body.Instructions[value]);
    }

    // Null, or the invariant culture, whose GetFormat gives no custom formatter.
    private bool HasNoCustomFormatter(CilInstruction provider) => provider switch
    {
        { Operation: CilOperation.LoadNull } => true,
        { Operation: CilOperation.Call } call => calledMethods.Resolve(call) is
        {
            Definition.Name: "get_InvariantCulture",
            DeclaringType.FullName: "System.Globalization.CultureInfo",
        },
        _ => false,
    };

    private static bool IsString(CliTypeIdentity type) => type.CanonicalName == "primitive:string";

    private MethodInstanceModel Helper(string name, CliTypeIdentity value)
    {
        var helper = _helpers.GetOrAdd(name, name => types.FindType(HelperType).Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == name));
        return methodInstances.ResolveMethodInstance(
            helper.Key.Assembly,
            helper.Key.MetadataToken,
            symbols.Format(helper),
            0,
            new CliGenericContext([], [value]));
    }
}
