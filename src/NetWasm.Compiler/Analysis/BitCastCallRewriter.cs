// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Unsafe.BitCast<TFrom, TTo> reads its argument through the address of a
// copy, which gives each caller a value frame and a store and load per call.
// The JIT reinterprets in place, and so does NetWasm: a double and a 64-bit
// integer, or a float and a 32-bit integer, become BitConverter's
// reinterpretation; two integers of one width, or one value type, need
// nothing; two integers of one width below 32 bits take the destination's
// conversion of the stack value; and two Vector128 or two Vector instantiations
// become the vector's own As. A struct value is typed by its storage, so a cast
// between two structs of one layout, as two spans, keeps its call: read in
// place, the value would reach a join typed as the source. Other pairs keep the
// call, which throws for types of different sizes as upstream's does, and so
// does the body of the method a cast would become.
internal sealed class BitCastCallRewriter(
    ICalledMethodResolver calledMethods,
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<(string Type, string Name, string Arguments), MethodInstanceModel> _targets = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (instruction.Operation != CilOperation.Call ||
                calledMethods.Resolve(instruction) is not
                {
                    Definition: { Name: "BitCast", IsStatic: true },
                    DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
                    MethodArguments: [var from, var to],
                })
            {
                continue;
            }
            if (Target(from, to) is not { } cast) continue;
            var (type, name, arguments, conversion) = cast;
            CilOperand replacement = (CilOperand?)conversion ?? new CilOperand.None();
            if (type is not null)
            {
                var method = Resolve(type, name!, arguments);
                if (method.Definition.Key == body.Method.Key) continue;
                replacement = new CilOperand.MethodInstance(method);
            }
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index] = instruction with
            {
                Operation = conversion is not null ? CilOperation.ConvertNumeric
                    : type is null ? CilOperation.Nop : CilOperation.Call,
                Operand = replacement,
            };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    // The method a cast becomes, by declaring type, name and type arguments;
    // the conversion it becomes, for two small integers; no type, name or
    // conversion when the stack holds both types alike; null when the cast keeps
    // its call.
    private static (string? Type, string? Name, ImmutableArray<CliTypeIdentity> Arguments,
        CilOperand.NumericConversion? Conversion)? Target(CliTypeIdentity from, CliTypeIdentity to) =>
        (from.CanonicalName, to.CanonicalName) switch
        {
            ("primitive:f8", "primitive:i8" or "primitive:u8") => ("System.BitConverter", "DoubleToInt64Bits", [], null),
            ("primitive:i8" or "primitive:u8", "primitive:f8") => ("System.BitConverter", "Int64BitsToDouble", [], null),
            ("primitive:f4", "primitive:i4" or "primitive:u4") => ("System.BitConverter", "SingleToInt32Bits", [], null),
            ("primitive:i4" or "primitive:u4", "primitive:f4") => ("System.BitConverter", "Int32BitsToSingle", [], null),
            ("primitive:i4" or "primitive:u4", "primitive:i4" or "primitive:u4") or
                ("primitive:i8" or "primitive:u8", "primitive:i8" or "primitive:u8") or
                ("primitive:f4", "primitive:f4") or ("primitive:f8", "primitive:f8") => (null, null, [], null),
            // One value type reads as itself, as a generic TChar or T does once it is byte or char.
            _ when from.IsValueType && from.CanonicalName == to.CanonicalName => (null, null, [], null),
            // A byte, sbyte or bool, or a short, ushort or char, is a 32-bit stack value: the destination's
            // conversion keeps the bits it has and extends them as the destination does.
            _ when SmallInteger(from) is { } source && SmallInteger(to) is { } destination &&
                source.Bits == destination.Bits =>
                (null, null, [], new(destination.Bits, destination.Unsigned, false, false, false)),
            _ when Vector(from) is { } vector && vector == Vector(to) =>
                (vector, "As", [from.TypeArguments[0], to.TypeArguments[0]], null),
            // Two 16-byte vectors, as Vector4 and Vector128<float>, are one v128.
            _ when Whole(from) && Whole(to) => (null, null, [], null),
            _ => null,
        };

    private static (int Bits, bool Unsigned)? SmallInteger(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i1" => (8, false),
        "primitive:u1" or "primitive:bool" => (8, true),
        "primitive:i2" => (16, false),
        "primitive:u2" or "primitive:char" => (16, true),
        _ => null,
    };

    // A vector value of 16 bytes; a Vector64 or Vector2 fills only 8 and a
    // Vector3 12, so their casts keep the call, which throws as upstream's does
    // for types of different sizes.
    private static bool Whole(CliTypeIdentity type) =>
        type.StackKind == CliValueKind.V128 && SimdVectorStorage.Width(type) == 16;

    // The static class whose As reinterprets a Vector128 or Vector instantiation.
    private static string? Vector(CliTypeIdentity type) =>
        type is { Shape: CliTypeShape.GenericInstantiation, ElementType.FullName: { } definition }
            ? definition switch
            {
                "System.Runtime.Intrinsics.Vector128`1" => "System.Runtime.Intrinsics.Vector128",
                "System.Numerics.Vector`1" => "System.Numerics.Vector",
                _ => null,
            }
            : null;

    private MethodInstanceModel Resolve(string type, string name, ImmutableArray<CliTypeIdentity> arguments) =>
        _targets.GetOrAdd((type, name, string.Join(",", arguments.Select(static argument => argument.CanonicalName))), _ =>
        {
            // Each is the only method of its name: BitConverter's conversions and the vectors' As.
            var method = types.FindType(type).Methods.Select(methods.GetMethod).Single(method => method.Name == name);
            return instances.ResolveMethodInstance(method.Key.Assembly, method.Key.MetadataToken, symbols.Format(method), 0,
                new CliGenericContext([], arguments));
        });
}
