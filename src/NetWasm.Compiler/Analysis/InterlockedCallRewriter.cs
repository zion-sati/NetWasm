// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Volatile's and Interlocked's [Intrinsic] calls become the loads and stores they are in a program that runs on one
// thread, as the JIT expands them, so every optimizer sees them and a null location is checked once, where the load
// or store dereferences it (the managed bodies checked it with ValidateLocation, and the load checked it again):
// Volatile.Read and Interlocked.Read are a volatile. ldobj, Volatile.Write a volatile. stobj; Exchange, Add,
// Increment, Decrement, And and Or load the location, store the new value and push the old one (Exchange, And, Or) or
// the new one, through locals of their own; CompareExchange is the compare-exchange instruction; MemoryBarrier is
// nothing. A generic Exchange, And or Or keeps its body for a type upstream does not take (it throws
// NotSupportedException), and a generic CompareExchange of a value type keeps the body that compares its bits.
internal sealed class InterlockedCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private static readonly FrozenSet<string> Primitives = new[]
    {
        "primitive:bool", "primitive:char", "primitive:u1", "primitive:i1", "primitive:i2", "primitive:u2",
        "primitive:i4", "primitive:u4", "primitive:i8", "primitive:u8", "primitive:nativeint", "primitive:nativeuint",
        "primitive:f4", "primitive:f8",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Integers = new[]
    {
        "primitive:i4", "primitive:u4", "primitive:i8", "primitive:u8",
    }.ToFrozenSet(StringComparer.Ordinal);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        List<CilInstruction>[]? replacements = null;
        var locals = new List<CliTypeIdentity>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var call = body.Instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { } target ||
                Lowering(target) is not { } lowering)
            {
                continue;
            }
            replacements ??= [.. body.Instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[index] = lowering(call, Local);
        }
        if (replacements is null)
        {
            return body;
        }
        return RenumberedInstructions.Splice(body, replacements) with
        {
            Locals = body.Locals.AddRange(locals.Select(static local => local.StackKind)),
            LocalSignatureTypes = body.LocalSignatureTypes.AddRange(locals),
            // An exchange holds the old value, the location and the new value at once, one more than the call's two.
            MaxStack = body.MaxStack + 1,
        };

        // A new local of the type, by its index.
        int Local(CliTypeIdentity type)
        {
            locals.Add(type);
            return body.Locals.Length + locals.Count - 1;
        }
    }

    // What the call becomes, given a way to make locals; null when the call stays.
    private Func<CilInstruction, Func<CliTypeIdentity, int>, List<CilInstruction>>? Lowering(MethodInstanceModel target)
    {
        var type = symbols.Format(target.Definition.DeclaringType);
        var name = target.Definition.Name;
        var generic = target.Definition.GenericArity != 0;
        if (type == "System.Threading.Volatile")
        {
            return name switch
            {
                "Read" when Location(target, generic) is { } read => (call, _) => Read(call, read),
                "Write" when Location(target, generic) is { } written => (call, _) => Write(call, written),
                _ => null,
            };
        }
        if (type != "System.Threading.Interlocked")
        {
            return null;
        }
        if (name is "MemoryBarrier" or "MemoryBarrierProcessWide")
        {
            return static (call, _) => [call with { Operation = CilOperation.Nop, Operand = new CilOperand.None() }];
        }
        if (name is not ("Read" or "Exchange" or "CompareExchange" or "And" or "Or" or "Add" or "Increment" or "Decrement") ||
            Location(target, generic) is not { } value)
        {
            return null;
        }
        return name switch
        {
            "Read" => (call, _) => Read(call, value),
            "Exchange" when !generic || IsReference(value) || IsEnumOrPrimitive(value) =>
                (call, local) => Exchange(call, value, local),
            "And" or "Or" when (generic ? IsEnumOrInteger(value) : Integers.Contains(value.CanonicalName)) =>
                (call, local) => Combine(call, value, name == "And" ? CilOperation.BitwiseAnd : CilOperation.BitwiseOr, local),
            "Add" when Integers.Contains(value.CanonicalName) =>
                (call, local) => Add(call, value, null, local),
            "Increment" or "Decrement" when Integers.Contains(value.CanonicalName) =>
                (call, local) => Add(call, value, name == "Increment" ? 1 : -1, local),
            "CompareExchange" when (generic ? IsReference(value) : IsCompareExchanged(value)) =>
                (call, _) => [call with { Operation = CilOperation.CompareExchange, Operand = new CilOperand.TypeIdentity(value) }],
            _ => null,
        };
    }

    // The type of the location the call reads or writes: a generic method's one type argument, which a reached call
    // has closed, or what a method's first parameter points to.
    private static CliTypeIdentity? Location(MethodInstanceModel target, bool generic)
    {
        if (!generic)
        {
            return target.Signature.ParameterSignatureTypes is [var location, ..] &&
                location.Shape == CliTypeShape.ManagedByReference
                    ? location.ElementType
                    : null;
        }
        if (target.MethodArguments is not [var argument] ||
            argument.Shape is CliTypeShape.GenericMethodParameter or CliTypeShape.GenericTypeParameter)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"{target.Definition.Name} requires one closed type argument"));
        }
        return argument;
    }

    private static List<CilInstruction> Read(CilInstruction call, CliTypeIdentity value) =>
    [
        call with { Operation = CilOperation.Volatile, Operand = new CilOperand.None() },
        call with { Operation = CilOperation.LoadObject, Operand = new CilOperand.TypeIdentity(value) },
    ];

    private static List<CilInstruction> Write(CilInstruction call, CliTypeIdentity value) =>
    [
        call with { Operation = CilOperation.Volatile, Operand = new CilOperand.None() },
        call with { Operation = CilOperation.StoreObject, Operand = new CilOperand.TypeIdentity(value) },
    ];

    // The old value, after the new one is stored.
    private static List<CilInstruction> Exchange(CilInstruction call, CliTypeIdentity value, Func<CliTypeIdentity, int> local)
    {
        var (location, operand) = (local(CliTypeIdentity.ManagedByReference(value)), local(value));
        return
        [
            Store(call, operand), Store(call, location),
            Load(call, location), Object(call, CilOperation.LoadObject, value),
            Load(call, location), Load(call, operand), Object(call, CilOperation.StoreObject, value),
        ];
    }

    // The old value, after the value it combines to is stored.
    private static List<CilInstruction> Combine(
        CilInstruction call, CliTypeIdentity value, CilOperation operation, Func<CliTypeIdentity, int> local)
    {
        var (location, operand, old) = (local(CliTypeIdentity.ManagedByReference(value)), local(value), local(value));
        return
        [
            Store(call, operand), Store(call, location),
            Load(call, location), Object(call, CilOperation.LoadObject, value), Store(call, old),
            Load(call, location), Load(call, old), Load(call, operand), call with { Operation = operation, Operand = new CilOperand.None() },
            Object(call, CilOperation.StoreObject, value),
            Load(call, old),
        ];
    }

    // The new value, after it is stored: the operand added, or one added or subtracted.
    private static List<CilInstruction> Add(
        CilInstruction call, CliTypeIdentity value, int? step, Func<CliTypeIdentity, int> local)
    {
        var wide = value.StackKind == CliValueKind.I8;
        var location = local(CliTypeIdentity.ManagedByReference(value));
        var sum = local(value);
        var instructions = new List<CilInstruction>();
        int? operand = null;
        if (step is null)
        {
            operand = local(value);
            instructions.Add(Store(call, operand.Value));
        }
        instructions.Add(Store(call, location));
        instructions.Add(Load(call, location));
        instructions.Add(Object(call, CilOperation.LoadObject, value));
        instructions.Add(operand is { } index
            ? Load(call, index)
            : wide
                ? call with { Operation = CilOperation.LoadInt64, Operand = new CilOperand.ConstantI8(1) }
                : call with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(1) });
        instructions.Add(call with
        {
            Operation = step == -1 ? CilOperation.Subtract : CilOperation.Add,
            Operand = new CilOperand.None(),
        });
        instructions.Add(Store(call, sum));
        instructions.Add(Load(call, location));
        instructions.Add(Load(call, sum));
        instructions.Add(Object(call, CilOperation.StoreObject, value));
        instructions.Add(Load(call, sum));
        return instructions;
    }

    private static CilInstruction Store(CilInstruction call, int local) =>
        call with { Operation = CilOperation.StoreLocal, Operand = new CilOperand.Index(local) };

    private static CilInstruction Load(CilInstruction call, int local) =>
        call with { Operation = CilOperation.LoadLocal, Operand = new CilOperand.Index(local) };

    private static CilInstruction Object(CilInstruction call, CilOperation operation, CliTypeIdentity value) =>
        call with { Operation = operation, Operand = new CilOperand.TypeIdentity(value) };

    private static bool IsReference(CliTypeIdentity type) =>
        !type.IsValueType && type.Shape is not (CliTypeShape.GenericMethodParameter or CliTypeShape.GenericTypeParameter);

    // An enum's identity carries its underlying integer as its stack kind, where a struct's is a value type; of the
    // structs, only the runtime handles carry one too, and upstream takes neither.
    private static bool IsEnum(CliTypeIdentity type) =>
        type is { IsValueType: true, Shape: CliTypeShape.Named, StackKind: CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt } &&
        type.FullName is not ("System.RuntimeMethodHandle" or "System.RuntimeFieldHandle");

    private static bool IsEnumOrPrimitive(CliTypeIdentity type) =>
        Primitives.Contains(type.CanonicalName) || IsEnum(type);

    private static bool IsEnumOrInteger(CliTypeIdentity type) =>
        IsEnum(type) || type.CanonicalName is "primitive:u1" or "primitive:i1" or "primitive:i2" or "primitive:u2" or
            "primitive:i4" or "primitive:u4" or "primitive:i8" or "primitive:u8" or "primitive:nativeint" or
            "primitive:nativeuint";

    // The values the compare-exchange instruction takes: integers of every width, floats by their bits, references.
    private static bool IsCompareExchanged(CliTypeIdentity type) =>
        IsReference(type) || type.CanonicalName is "primitive:u1" or "primitive:i1" or "primitive:i2" or "primitive:u2" or
            "primitive:i4" or "primitive:u4" or "primitive:i8" or "primitive:u8" or "primitive:nativeint" or
            "primitive:nativeuint" or "primitive:f4" or "primitive:f8";
}
