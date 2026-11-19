// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// The variadic arguments of a call into a C variadic function, as Wasm's C ABI passes them: one after another in a
// buffer, each at its own alignment, an integer of 32 bits or fewer as 32 bits, a float as a double (C's default
// promotions), a pointer at the target's pointer size; the call takes the buffer's address. The buffer is 16-byte
// aligned, as clang's caller aligns it, and laid out for wasm32 and for wasm64, which sizeof(nint) selects; the
// compiler folds it for the target it compiles.
internal static class NativeVariadicArguments
{
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);

    // The buffer's local and the elements that fill it, given each variadic argument's load and native type.
    public static (CilBodyLocal Buffer, ImmutableArray<CilBodyElement> Store) Plan(
        IReadOnlyList<(ImmutableArray<CilBodyElement> Load, CliTypeIdentity Type)> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var buffer = new CilBodyLocal(NativeInt);
        var narrow = Layout(arguments, pointerSize: 4);
        var wide = Layout(arguments, pointerSize: 8);
        var wideStores = new CilBodyLabel();
        var done = new CilBodyLabel();
        var elements = new List<CilBodyElement>
        {
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(Math.Max(wide.Size, 8) + 15)),
            Op(CilOperation.ConvertNativeUInt),
            Op(CilOperation.LocalAllocate),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(15)),
            Op(CilOperation.ConvertNativeInt),
            Op(CilOperation.Add),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(-16)),
            Op(CilOperation.ConvertNativeInt),
            Op(CilOperation.BitwiseAnd),
            Store(buffer),
            Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(NativeInt)),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(8)),
            Branch(CilOperation.BranchIfEqual, wideStores),
        };
        elements.AddRange(Stores(arguments, narrow.Offsets, buffer));
        elements.Add(Branch(CilOperation.Branch, done));
        elements.Add(Mark(wideStores));
        elements.AddRange(Stores(arguments, wide.Offsets, buffer));
        elements.Add(Mark(done));
        return (buffer, [.. elements]);
    }

    // Each argument's offset and the buffer's size for a target's pointer size.
    internal static (ImmutableArray<int> Offsets, int Size) Layout(
        IReadOnlyList<(ImmutableArray<CilBodyElement> Load, CliTypeIdentity Type)> arguments, int pointerSize)
    {
        var offsets = ImmutableArray.CreateBuilder<int>(arguments.Count);
        var offset = 0;
        foreach (var (_, type) in arguments)
        {
            var size = Size(type, pointerSize);
            offset = (offset + size - 1) / size * size;
            offsets.Add(offset);
            offset += size;
        }
        return (offsets.MoveToImmutable(), offset);
    }

    private static IEnumerable<CilBodyElement> Stores(
        IReadOnlyList<(ImmutableArray<CilBodyElement> Load, CliTypeIdentity Type)> arguments, ImmutableArray<int> offsets,
        CilBodyLocal buffer)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var (load, type) = arguments[index];
            yield return Load(buffer);
            yield return Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(offsets[index]));
            yield return Op(CilOperation.Add);
            foreach (var element in load) yield return element;
            if (type.StackKind == CliValueKind.F4) yield return Op(CilOperation.ConvertFloat64);
            yield return Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(Stored(type)));
        }
    }

    private static int Size(CliTypeIdentity type, int pointerSize) => type.StackKind switch
    {
        CliValueKind.I4 => 4,
        CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 => 8,
        CliValueKind.NativeInt => pointerSize,
        _ => throw new ArgumentException($"A variadic argument of type '{type.CanonicalName}' has no C promotion.",
            nameof(type)),
    };

    // The type each argument is stored as: promoted, as stind.i4, stind.i8, stind.r8 or stind.i store.
    private static CliTypeIdentity Stored(CliTypeIdentity type) => type.StackKind switch
    {
        CliValueKind.I4 => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        CliValueKind.I8 => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        CliValueKind.F4 or CliValueKind.F8 => CliTypeIdentity.Primitive("f8", CliValueKind.F8),
        _ => CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
    };
}
