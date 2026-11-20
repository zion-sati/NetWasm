// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

namespace NetWasm.Compiler.Metadata.Tests;

// Variadic arguments go in a buffer one after another, each at its own alignment: an int as 32 bits, a long as 64, a
// float promoted to a double, a pointer at the target's size. Both layouts are emitted; sizeof(nint) picks one.
public sealed class NativeVariadicArgumentsTests
{
    private static readonly ImmutableArray<CilBodyElement> NoLoad = [];

    private static (ImmutableArray<CilBodyElement> Load, CliTypeIdentity Type)[] Arguments(params CliValueKind[] kinds) =>
        [.. kinds.Select(kind => (NoLoad, CliTypeIdentity.FromStackKind(kind)))];

    [Fact]
    public void EachArgumentTakesItsOwnAlignmentForTheTarget()
    {
        var arguments = Arguments(CliValueKind.I4, CliValueKind.NativeInt, CliValueKind.I8, CliValueKind.F4, CliValueKind.F8,
            CliValueKind.I4);

        var (narrow, narrowSize) = NativeVariadicArguments.Layout(arguments, pointerSize: 4);
        var (wide, wideSize) = NativeVariadicArguments.Layout(arguments, pointerSize: 8);

        Assert.Equal([0, 4, 8, 16, 24, 32], narrow);
        Assert.Equal(36, narrowSize);
        Assert.Equal([0, 8, 16, 24, 32, 40], wide);
        Assert.Equal(44, wideSize);
    }

    [Fact]
    public void ThePlanAllocatesAnAlignedBufferAndStoresBothLayouts()
    {
        var (buffer, store) = NativeVariadicArguments.Plan(Arguments(CliValueKind.I4, CliValueKind.F4, CliValueKind.NativeInt));

        Assert.Equal(CliValueKind.NativeInt, buffer.Type.StackKind);
        var instructions = store.OfType<CilBodyElement.Instruction>().ToArray();
        // Room for the wide layout (4 + 8 + 8 = 24, rounded to 16-byte alignment with 15 to spare).
        Assert.Equal(new CilOperand.ConstantI4(24 + 15), instructions[0].Operand);
        Assert.Equal(CilOperation.LocalAllocate, instructions[2].Operation);
        Assert.Equal(new CilOperand.ConstantI4(-16), instructions[6].Operand);
        Assert.Contains(instructions, instruction => instruction is { Operation: CilOperation.SizeOf });
        // The float is promoted in each layout, and each layout stores three values.
        Assert.Equal(2, instructions.Count(instruction => instruction.Operation == CilOperation.ConvertFloat64));
        Assert.Equal(6, instructions.Count(instruction => instruction.Operation == CilOperation.StoreObject));
        Assert.Equal(["i4", "f8", "nativeint", "i4", "f8", "nativeint"], instructions
            .Where(instruction => instruction.Operation == CilOperation.StoreObject)
            .Select(instruction => ((CilOperand.TypeIdentity)instruction.Operand).Value.CanonicalName["primitive:".Length..]));
        // The int at 0, the promoted float at 8 and the pointer at 16, in both layouts.
        var offsets = Enumerable.Range(1, instructions.Length - 1)
            .Where(index => instructions[index].Operation == CilOperation.Add &&
                instructions[index - 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: not 15 } })
            .Select(index => ((CilOperand.ConstantI4)instructions[index - 1].Operand).Value);
        Assert.Equal([0, 8, 16, 0, 8, 16], offsets);
    }

    // Each argument's own load comes after its slot's address, and a long is stored as 64 bits.
    [Fact]
    public void EachStoreLoadsItsArgumentIntoItsSlot()
    {
        ImmutableArray<CilBodyElement> load = [CilBodyElements.Argument(3)];

        var (_, store) = NativeVariadicArguments.Plan([(load, CliTypeIdentity.FromStackKind(CliValueKind.I8))]);

        var instructions = store.OfType<CilBodyElement.Instruction>().ToArray();
        var stores = Enumerable.Range(0, instructions.Length)
            .Where(index => instructions[index].Operation == CilOperation.StoreObject).ToArray();
        Assert.Equal(2, stores.Length);
        Assert.All(stores, index =>
        {
            Assert.Equal(load[0], instructions[index - 1]);
            Assert.Equal("primitive:i8", ((CilOperand.TypeIdentity)instructions[index].Operand).Value.CanonicalName);
        });
    }

    [Fact]
    public void AnArgumentWithoutAPromotionIsRejected()
    {
        Assert.Throws<ArgumentException>(() => NativeVariadicArguments.Plan(Arguments(CliValueKind.ValueType)));
        Assert.Throws<ArgumentNullException>(() => NativeVariadicArguments.Plan(null!));
        var (none, size) = NativeVariadicArguments.Layout(Arguments(), 4);
        Assert.Empty(none);
        Assert.Equal(0, size);
    }
}
