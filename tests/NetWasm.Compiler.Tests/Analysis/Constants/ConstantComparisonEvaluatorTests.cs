// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;
using static ConstantOperandReaderTests;

public sealed class ConstantComparisonEvaluatorTests
{
    internal static readonly CliTypeIdentity Text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
    private static readonly CliTypeIdentity Extensions = CliTypeIdentity.Named(Assembly, "System", "MemoryExtensions", false);

    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new ConstantComparisonEvaluator(null!));
        var comparisons = AsComparisons(Comparisons(new MethodFacts()));
        Assert.Throws<ArgumentNullException>(() => comparisons.Evaluate(null!, []));
        Assert.Throws<ArgumentNullException>(() => comparisons.Evaluate(StringMethod("op_Equality", true, Text, Text), null!));
    }

    [Theory]
    [InlineData("op_Equality", null, null, 1)]
    [InlineData("op_Equality", "a", "a", 1)]
    [InlineData("op_Equality", "a", "b", 0)]
    [InlineData("op_Equality", "a", null, 0)]
    [InlineData("op_Equality", null, "a", 0)]
    [InlineData("Equals", "a", "a", 1)]
    [InlineData("Equals", null, "a", 0)]
    [InlineData("op_Inequality", "a", "a", 0)]
    [InlineData("op_Inequality", "a", "b", 1)]
    [InlineData("op_Inequality", null, null, 0)]
    public void ComparesStringLiteralsOrdinally(string name, string? left, string? right, int expected)
    {
        var result = AsComparisons(Comparisons(new MethodFacts())).Evaluate(StringMethod(name, true, Text, Text), [Literal(left), Literal(right)]);

        Assert.Equal(new ScalarConstant.Int32(expected), result);
    }

    [Theory]
    [InlineData("a", "a", 1)]
    [InlineData("a", "b", 0)]
    [InlineData("a", null, 0)]
    public void ComparesALiteralReceiverWithInstanceEquals(string receiver, string? argument, int expected)
    {
        var result = AsComparisons(Comparisons(new MethodFacts())).Evaluate(StringMethod("Equals", false, Text), [Literal(receiver), Literal(argument)]);

        Assert.Equal(new ScalarConstant.Int32(expected), result);
    }

    [Fact]
    public void DeclinesUnprovenStringComparisons()
    {
        var comparisons = AsComparisons(Comparisons(new MethodFacts()));
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);

        // A null receiver throws.
        Assert.Null(comparisons.Evaluate(StringMethod("Equals", false, Text), [new ScalarConstant.Null(), Literal("a")]));
        Assert.Null(comparisons.Evaluate(StringMethod("op_Equality", true, Text, Text), [new ScalarConstant.Int32(1), Literal("a")]));
        Assert.Null(comparisons.Evaluate(StringMethod("op_Inequality", true, Text, Text), [Literal("a")]));
        Assert.Null(comparisons.Evaluate(StringMethod("op_Equality", true, Text), [Literal("a")]));
        Assert.Null(comparisons.Evaluate(StringMethod("op_Equality", true, Text, integer), [Literal("a"), new ScalarConstant.Int32(1)]));
        Assert.Null(comparisons.Evaluate(StringMethod("op_Inequality", true, Text), [Literal("a")]));
        Assert.Null(comparisons.Evaluate(StringMethod("Equals", false, integer), [Literal("a"), new ScalarConstant.Int32(1)]));
        Assert.Null(comparisons.Evaluate(StringMethod("Concat", true, Text, Text), [Literal("a"), Literal("b")]));
        var foreign = StringMethod("op_Equality", true, Text, Text);
        foreign = foreign with { Definition = foreign.Definition with { Key = new(new AssemblyIdentity("Other"), 0x06000001) } };
        Assert.Null(comparisons.Evaluate(foreign, [Literal("a"), Literal("a")]));
    }

    [Theory]
    [InlineData(false, new byte[] { 1, 2 }, new byte[] { 1, 2 }, 1)]
    [InlineData(false, new byte[] { 1, 2 }, new byte[] { 1, 3 }, 0)]
    [InlineData(false, new byte[] { 1 }, new byte[] { 1, 0 }, 0)]
    [InlineData(true, new byte[] { 7 }, new byte[] { 7 }, 1)]
    public void ComparesByteSpanContents(bool generic, byte[] left, byte[] right, int expected)
    {
        var element = generic ? CliTypeIdentity.GenericParameter(method: true, 0) : Byte;
        var method = SequenceEqual([Span(element), Span(element)], [Byte]);

        Assert.Equal(new ScalarConstant.Int32(expected), AsComparisons(Comparisons(new MethodFacts()))
            .Evaluate(method, [new ScalarConstant.Bytes([.. left]), new ScalarConstant.Bytes([.. right])]));
    }

    [Fact]
    public void DeclinesOtherSequenceEqualShapes()
    {
        var comparisons = AsComparisons(Comparisons(new MethodFacts()));
        ScalarConstant[] bytes = [new ScalarConstant.Bytes([1]), new ScalarConstant.Bytes([1])];
        var integer = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var unbound = CliTypeIdentity.GenericParameter(method: true, 1);

        Assert.Null(comparisons.Evaluate(SequenceEqual([Span(Byte), Span(Byte)], [Byte]), [new ScalarConstant.Null(), bytes[1]]));
        Assert.Null(comparisons.Evaluate(SequenceEqual([Span(integer), Span(integer)], [integer]), bytes));
        Assert.Null(comparisons.Evaluate(SequenceEqual([Span(unbound), Span(unbound)], [Byte]), bytes));
        Assert.Null(comparisons.Evaluate(SequenceEqual([Byte, Byte], [Byte]), bytes));
        Assert.Null(comparisons.Evaluate(SequenceEqual([Span(Byte), Span(Byte), Span(Byte)], [Byte]), [.. bytes, bytes[0]]));
        var instance = SequenceEqual([Span(Byte), Span(Byte)], [Byte]);
        Assert.Null(comparisons.Evaluate(instance with { Definition = instance.Definition with { IsStatic = false } }, bytes));
        Assert.Null(comparisons.Evaluate(instance with { DeclaringType = Owner }, bytes));
    }

    internal static ScalarConstant Literal(string? value) => value is null ? new ScalarConstant.Null() : new ScalarConstant.String(value);

    internal static MethodInstanceModel StringMethod(string name, bool isStatic, params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.I4), [.. parameters]);
        return new(new(new(Assembly, 0x06000200), TypeKey, name, isStatic, signature, 1), Text, [], signature);
    }

    internal static MethodInstanceModel SequenceEqual(CliTypeIdentity[] parameters, ImmutableArray<CliTypeIdentity> methodArguments)
    {
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.I4), [.. parameters]);
        return new(new(new(Assembly, 0x06000300), TypeKey, "SequenceEqual", true, signature, 1), Extensions, methodArguments, signature);
    }

    private static IConstantComparisonEvaluator AsComparisons(IConstantComparisonEvaluator comparisons) => comparisons;
}
