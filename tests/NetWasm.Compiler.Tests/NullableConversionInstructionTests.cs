// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq.Expressions.Interpreter;

namespace NetWasm.Compiler.Tests;

public sealed class NullableConversionInstructionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericConversionPreservesPresentValues(bool liftedToNull)
    {
        var instruction = new NumericConvertInstruction(TypeCode.Int32, TypeCode.Int64, liftedToNull);
        var frame = new InterpretedFrame([], 0, 1);
        frame.Push(42);

        Assert.Equal(1, instruction.Run(frame));

        Assert.Equal(1, instruction.ConsumedStack);
        Assert.Equal(1, instruction.ProducedStack);
        Assert.Equal(1, frame.StackIndex);
        Assert.Equal(42L, Assert.IsType<long>(frame.Pop()));
    }

    [Fact]
    public void LiftedNumericConversionPropagatesNull()
    {
        var instruction = new NumericConvertInstruction(TypeCode.Int32, TypeCode.Int64, true);
        var frame = new InterpretedFrame([], 0, 1);
        frame.Push(null);

        Assert.Equal(1, instruction.Run(frame));
        Assert.Equal(1, frame.StackIndex);
        Assert.Null(frame.Pop());
    }

    [Fact]
    public void NonNullableNumericConversionRejectsNull()
    {
        var instruction = new NumericConvertInstruction(TypeCode.Int32, TypeCode.Int64, false);
        var frame = new InterpretedFrame([], 0, 1);
        frame.Push(null);

        Assert.Throws<InvalidOperationException>(() => instruction.Run(frame));
        Assert.Equal(0, frame.StackIndex);
    }

    [Fact]
    public void NullableValueGuardPreservesTheBoxedUnderlyingValue()
    {
        var instruction = new NullableValueInstruction();
        var frame = new InterpretedFrame([], 0, 1);
        object value = true;
        frame.Push(value);

        Assert.Equal(1, instruction.Run(frame));
        Assert.Equal(1, instruction.ConsumedStack);
        Assert.Equal(1, instruction.ProducedStack);
        Assert.Equal(1, frame.StackIndex);
        Assert.Same(value, frame.Pop());
    }

    [Fact]
    public void NullableValueGuardRejectsNull()
    {
        var instruction = new NullableValueInstruction();
        var frame = new InterpretedFrame([], 0, 1);
        frame.Push(null);

        Assert.Throws<InvalidOperationException>(() => instruction.Run(frame));
        Assert.Equal(0, frame.StackIndex);
    }
}
