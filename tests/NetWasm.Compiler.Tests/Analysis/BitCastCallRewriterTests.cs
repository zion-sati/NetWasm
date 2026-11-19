// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis;

// Which Unsafe.BitCast calls read their value in place: one value type as itself, and two small integers of one width
// through the destination's conversion. Two structs of one layout, as two spans, keep the call: a struct value is typed
// by its storage, which reading it in place would leave as the source's. Other pairs keep the call, which throws for
// types of different sizes as upstream's does.
public sealed class BitCastCallRewriterTests
{
    private static readonly AssemblyIdentity Core = new("NetWasm.CoreLib");

    public static TheoryData<string, string, string> Casts() => new()
    {
        // One value type, as a generic TChar once it is char.
        { "u1", "u1", "nop" },
        { "char", "char", "nop" },
        { "Point", "Point", "nop" },
        { "ReadOnlySpan<char>", "ReadOnlySpan<char>", "nop" },
        // Small integers of one width: the destination's conversion of the stack value.
        { "i1", "u1", "conv 8 unsigned" },
        { "u1", "i1", "conv 8 signed" },
        { "bool", "u1", "conv 8 unsigned" },
        { "u1", "bool", "conv 8 unsigned" },
        { "char", "i2", "conv 16 signed" },
        { "i2", "char", "conv 16 unsigned" },
        { "i2", "u2", "conv 16 unsigned" },
        // Structs of one layout keep their storage's type, so their calls stay, as everything else's does.
        { "ReadOnlySpan<char>", "ReadOnlySpan<u2>", "call" },
        { "Span<char>", "ReadOnlySpan<char>", "call" },
        { "Vector64<i4>", "Vector64<f4>", "call" },
        { "Vector256<i4>", "Vector256<u4>", "call" },
        { "Vector512<i8>", "Vector512<f8>", "call" },
        { "i1", "i2", "call" },
        { "Point", "Other", "call" },
        { "ReadOnlySpan<char>", "Vector256<i4>", "call" },
        { "Vector64<i4>", "Vector256<i4>", "call" },
        { "string", "string", "call" },
    };

    [Theory]
    [MemberData(nameof(Casts))]
    public void ReadsInPlaceOnlyWhereTheLayoutAgrees(string from, string to, string expected)
    {
        var call = new CilInstruction(0, 1, CilOperation.Call, new CilOperand.MethodInstance(BitCast(Type(from), Type(to))));
        var body = new CilMethodBody(Method("Caller"), 2, [], [call, new(1, 2, CilOperation.Return, new CilOperand.None())]);

        var result = new BitCastCallRewriter(new CalledMethods(), new Unused(), new Unused(), new Unused(), new Unused())
            .Rewrite(body);

        var rewritten = result.Instructions[0];
        Assert.Equal(expected, rewritten switch
        {
            { Operation: CilOperation.Nop } => "nop",
            { Operation: CilOperation.ConvertNumeric, Operand: CilOperand.NumericConversion conversion } =>
                $"conv {conversion.BitWidth} {(conversion.DestinationUnsigned ? "unsigned" : "signed")}",
            { Operation: CilOperation.Call } => "call",
            _ => rewritten.Operation.ToString(),
        });
        Assert.Equal(expected == "call", ReferenceEquals(body, result));
    }

    private static CliTypeIdentity Type(string name)
    {
        if (name.IndexOf('<', StringComparison.Ordinal) is var open and > 0)
        {
            var definition = name[..open];
            var ns = definition.Contains("Span", StringComparison.Ordinal) ? "System" : "System.Runtime.Intrinsics";
            return CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Core, ns, definition + "`1", isValueType: true), [Type(name[(open + 1)..^1])]);
        }
        return name switch
        {
            "Point" or "Other" => CliTypeIdentity.Named(Core, "Fixture", name, isValueType: true),
            "string" => CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false),
            "i8" or "u8" => CliTypeIdentity.Primitive(name, CliValueKind.I8),
            "f4" => CliTypeIdentity.Primitive(name, CliValueKind.F4),
            "f8" => CliTypeIdentity.Primitive(name, CliValueKind.F8),
            _ => CliTypeIdentity.Primitive(name, CliValueKind.I4),
        };
    }

    private static MethodInstanceModel BitCast(CliTypeIdentity from, CliTypeIdentity to)
    {
        var definition = Method("BitCast") with { GenericArity = 2 };
        return new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Core, "System.Runtime.CompilerServices", "Unsafe", isValueType: false), [from, to],
            definition.Signature);
    }

    private static MethodDefinitionModel Method(string name) => new(
        new(Core, 0x06000001), new(Core, 0x02000001), name, true,
        MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void)), 1);

    private sealed class CalledMethods : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance { Value: var method } ? method : null;
    }

    // No cast here becomes a call to another method.
    private sealed class Unused : ITypeFinder, IMethodRepository, IMethodInstanceResolver, ISymbolFormatter
    {
        public TypeDefinitionModel FindType(string fullName) => throw new InvalidOperationException();
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new InvalidOperationException();
        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken, string methodDisplayName,
            int ilOffset, CliGenericContext? genericContext = null) => throw new InvalidOperationException();
        public string Format(EntityKey key) => throw new InvalidOperationException();
        public string Format(MethodDefinitionModel method) => throw new InvalidOperationException();
    }
}
