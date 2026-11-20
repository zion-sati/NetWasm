// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// The CIL that copies a struct between its managed value and its native copy (NativeStructLayout), field by field, as
// desktop .NET's layout-class marshaler does: a bool or char converts (NativeScalarConversions), a nested struct whose
// native bytes differ copies its own fields, and every other field copies as it is. The native copy lives in the buffer
// NativeStructBuffer allocates. A struct holding a pointer-sized field lays out differently on wasm32 and wasm64, so
// its copies choose by sizeof(nint), which the compiler folds for the target.
internal sealed class NativeStructCopier : INativeStructCopier
{
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);

    private readonly CliTypeIdentity _type;
    private readonly NativeStructLayout _narrow;
    private readonly NativeStructLayout _wide;
    private readonly NativeScalarConversions _conversions;

    public NativeStructCopier(CliTypeIdentity type, NativeStructLayout narrow, NativeStructLayout wide,
        NativeScalarConversions conversions)
    {
        _type = type;
        _narrow = narrow;
        _wide = wide;
        _conversions = conversions;
    }

    public int NativeSize => Math.Max(_narrow.Size, _wide.Size);

    public ImmutableArray<CilBodyElement> Copy(ImmutableArray<CilBodyElement> managed, CilBodyLocal buffer,
        NativeCopyDirection direction) =>
        ByPointerSize(layout => CopyFields(layout, _type, managed, [], buffer, 0, direction == NativeCopyDirection.ToNative));

    private ImmutableArray<CilBodyElement> ByPointerSize(Func<NativeStructLayout, IEnumerable<CilBodyElement>> copy)
    {
        if (Same(_narrow, _wide))
        {
            return [.. copy(_narrow)];
        }
        var wide = new CilBodyLabel();
        var done = new CilBodyLabel();
        return
        [
            Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(NativeInt)),
            Constant(8),
            Branch(CilOperation.BranchIfEqual, wide),
            .. copy(_narrow),
            Branch(CilOperation.Branch, done),
            Mark(wide),
            .. copy(_wide),
            Mark(done),
        ];
    }

    private IEnumerable<CilBodyElement> CopyFields(NativeStructLayout layout, CliTypeIdentity owner,
        ImmutableArray<CilBodyElement> managed, ImmutableArray<CilBodyElement> path, CilBodyLocal buffer, int offset,
        bool toNative)
    {
        foreach (var field in layout.Fields)
        {
            var instance = new CilOperand.FieldInstance(new FieldInstanceModel(field.Field, owner, field.Type));
            if (field.Nested is { CopiesWhole: false } nested)
            {
                foreach (var element in CopyFields(nested, field.Type, managed, path.Add(Op(CilOperation.LoadFieldAddress, instance)),
                    buffer, offset + field.Offset, toNative))
                {
                    yield return element;
                }
                continue;
            }
            var native = field.Conversion?.Native ?? field.Type;
            CilBodyElement[] address =
                [Load(buffer), Constant(offset + field.Offset), Op(CilOperation.ConvertNativeInt), Op(CilOperation.Add)];
            IEnumerable<CilBodyElement> elements = toNative
                ?
                [
                    .. address, .. managed, .. path, Op(CilOperation.LoadField, instance),
                    .. field.Conversion is { } toForm ? _conversions.ToNative(toForm) : [],
                    Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(native)),
                ]
                :
                [
                    .. managed, .. path, Op(CilOperation.LoadFieldAddress, instance), .. address,
                    Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(native)),
                    .. field.Conversion is { } fromForm ? _conversions.ToManaged(fromForm) : [],
                    Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(field.Type)),
                ];
            foreach (var element in elements)
            {
                yield return element;
            }
        }
    }

    // Whether the struct's layouts at the two pointer sizes place every field alike, so one copy serves both. They are
    // one struct's, so they hold the same fields, and a field that converts as a nested struct does in both.
    private static bool Same(NativeStructLayout left, NativeStructLayout right) =>
        left.Size == right.Size &&
        left.Fields.Zip(right.Fields).All(pair => pair.First.Offset == pair.Second.Offset &&
            pair.First.Size == pair.Second.Size &&
            (pair.First.Nested is not { } nested || Same(nested, pair.Second.Nested!)));

    private static CilBodyElement Constant(int value) => Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));
}
