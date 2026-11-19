// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeProvidedMembers.NativeMarshalling;

// A struct that converts copies between its managed value and its native copy field by field, once when it lays out
// alike at both pointer sizes and otherwise by sizeof(nint). Only bool fields are used: their conversion needs no
// runtime members.
public sealed class NativeStructCopierTests
{
    private static readonly CliTypeIdentity Bool = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
    private static readonly NativeScalarConversions Conversions = new(null!, BestFit: false, ThrowOnUnmappableChar: false);

    // Only a struct that lays out natively and converts has a copier.
    [Fact]
    public void TheFactoryMakesCopiersOnlyForStructsThatConvert()
    {
        var source = new NativeStructSource();
        var flagged = source.Add("Flagged", fields: [("A", Int32, null), ("B", Bool, null)]);
        var point = source.Add("Point", fields: [("X", Int32, null)]);
        var unknown = CliTypeIdentity.Named(NativeStructSource.Assembly, "Native", "Unknown", true);
        var factory = new NativeStructCopierFactory(source);

        Assert.Throws<ArgumentNullException>(() => new NativeStructCopierFactory(null!));
        Assert.Equal(8, factory.Create(flagged, Conversions)!.NativeSize);
        Assert.Null(factory.Create(point, Conversions));
        Assert.Null(factory.Create(unknown, Conversions));
    }

    // To native, each field is read from the managed value and stored at its offset in the buffer, a bool as 1 or 0
    // in a 4-byte BOOL; to managed, each is read from the buffer and stored into the value, a BOOL as true when not 0.
    [Fact]
    public void AStructLaidOutAlikeAtBothPointerSizesCopiesOnce()
    {
        var source = new NativeStructSource();
        var flagged = source.Add("Flagged", fields: [("A", Int32, null), ("B", Bool, null)]);
        var copier = new NativeStructCopierFactory(source).Create(flagged, Conversions)!;
        var buffer = new CilBodyLocal(NativeInt);

        var toNative = Operations(copier.Copy([Argument(0)], buffer, NativeCopyDirection.ToNative));
        var toManaged = Operations(copier.Copy([Argument(0)], buffer, NativeCopyDirection.ToManaged));

        Assert.DoesNotContain(CilOperation.SizeOf, toNative);
        Assert.Equal(["i4", "i4"], Stored(copier.Copy([Argument(0)], buffer, NativeCopyDirection.ToNative)));
        Assert.Equal(1, toNative.Count(operation => operation == CilOperation.CompareGreaterThanUnsigned));
        Assert.Equal(["i4", "bool"], Stored(copier.Copy([Argument(0)], buffer, NativeCopyDirection.ToManaged)));
        Assert.Equal(1, toManaged.Count(operation => operation == CilOperation.CompareGreaterThanUnsigned));
    }

    // A struct whose layout differs between wasm32 and wasm64 (sizes, an offset, a field's size, or a nested struct's
    // layout) copies by sizeof(nint), which the compiler folds for the target; one that does not copies once.
    [Theory]
    [InlineData("PointerSized", true)]
    [InlineData("OffsetMoves", true)]
    [InlineData("FieldGrows", true)]
    [InlineData("NestedGrows", true)]
    [InlineData("NestedAlike", false)]
    public void AStructLaidOutDifferentlyByPointerSizeCopiesByTheTargetsPointerSize(string name, bool split)
    {
        var source = new NativeStructSource();
        var grows = source.Add("Grows", declaredSize: 32, fields: [("P", NativeInt, null), ("B", Bool, null)]);
        var flag = source.Add("Flag", fields: [("B", Bool, null)]);
        var type = name switch
        {
            "PointerSized" => source.Add(name, fields: [("P", NativeInt, null), ("B", Bool, null)]),
            "OffsetMoves" => source.Add(name, declaredSize: 32, fields: [("A", Int32, null), ("P", NativeInt, null), ("B", Bool, null)]),
            "FieldGrows" => grows,
            "NestedGrows" => source.Add(name, fields: [("G", grows, null)]),
            _ => source.Add(name, fields: [("F", flag, null), ("X", Int32, null)]),
        };
        var copier = new NativeStructCopierFactory(source).Create(type, Conversions)!;

        foreach (var direction in new[] { NativeCopyDirection.ToNative, NativeCopyDirection.ToManaged })
        {
            var operations = Operations(copier.Copy([Argument(0)], new CilBodyLocal(NativeInt), direction));

            Assert.Equal(split, operations.Contains(CilOperation.SizeOf));
            if (split)
            {
                Assert.Equal([CilOperation.SizeOf, CilOperation.LoadInt32], operations.Take(2));
            }
            // A nested struct's fields are reached through its address in the managed value, beyond the address each
            // field is stored through to managed.
            var ownAddresses = direction == NativeCopyDirection.ToManaged
                ? operations.Count(operation => operation == CilOperation.StoreObject)
                : 0;
            Assert.Equal(name.StartsWith("Nested", StringComparison.Ordinal),
                operations.Count(operation => operation == CilOperation.LoadFieldAddress) > ownAddresses);
        }
    }

    private static CilBodyElement Argument(int index) => CilBodyElements.Argument(index);

    private static CilOperation[] Operations(ImmutableArray<CilBodyElement> elements) =>
        [.. elements.OfType<CilBodyElement.Instruction>().Select(instruction => instruction.Operation)];

    // The types each copied field is stored as, by its primitive name.
    private static string[] Stored(ImmutableArray<CilBodyElement> elements) =>
        [.. elements.OfType<CilBodyElement.Instruction>()
            .Where(instruction => instruction.Operation == CilOperation.StoreObject)
            .Select(instruction => ((CilOperand.TypeIdentity)instruction.Operand).Value.CanonicalName["primitive:".Length..])];
}
