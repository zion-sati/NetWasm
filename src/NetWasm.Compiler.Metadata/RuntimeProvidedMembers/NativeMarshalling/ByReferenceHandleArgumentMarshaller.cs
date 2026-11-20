// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A SafeHandle or CriticalHandle passed by reference, as desktop .NET's handle marshalers pass it:
// - In: the caller's handle passes its value; a SafeHandle is borrowed for the call and released after it.
// - Out: a new handle is made before the call, so no native resource exists that nothing owns, and the native
//   function writes the value. Afterwards the new handle takes that value and replaces the caller's, unless the
//   handle went in too and the value did not change, which would leave two handles owning one resource.
// The native function receives the address of a native-sized local holding the value.
internal static class ByReferenceHandleArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, CliTypeIdentity type, HandleKind kind, bool isIn, bool isOut, MethodInstanceModel? constructor, NativeMarshallingMembers members)
    {
        var nativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
        var input = new CilBodyLocal(type);
        var added = new CilBodyLocal(boolean);
        var output = new CilBodyLocal(type);
        var original = new CilBodyLocal(nativeInt);
        var marshalled = new CilBodyLocal(boolean);
        var native = new CilBodyLocal(nativeInt);
        var handle = new CilOperand.FieldInstance(members.HandleFields.For(kind));

        var marshal = new List<CilBodyElement>();
        if (isIn)
        {
            marshal.AddRange([
                Argument(argument),
                Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(type)),
                Store(input),
                Load(input),
            ]);
            marshal.AddRange(kind == HandleKind.Safe
                ? [Address(added), Call(members.SafeHandleAddRef)]
                : [Op(CilOperation.LoadField, handle)]);
            marshal.Add(Store(native));
        }
        if (isOut)
        {
            marshal.AddRange([Op(CilOperation.NewObject, new CilOperand.MethodInstance(constructor!)), Store(output)]);
            if (!isIn)
            {
                marshal.AddRange([Load(output), Op(CilOperation.LoadField, handle), Store(native)]);
            }
            marshal.AddRange([
                Load(native),
                Store(original),
                Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                Store(marshalled),
            ]);
        }

        // A CriticalHandle that went in is kept alive until the call returns; a SafeHandle's borrow keeps it.
        CilBodyElement[] unmarshal = isIn && kind == HandleKind.Critical ? [Load(input), Call(members.KeepAlive)] : [];

        var cleanup = new List<CilBodyElement>();
        if (isOut)
        {
            var unchanged = new CilBodyLabel();
            cleanup.AddRange([Load(marshalled), Branch(CilOperation.BranchIfFalse, unchanged)]);
            if (isIn)
            {
                cleanup.AddRange([Load(native), Load(original), Branch(CilOperation.BranchIfEqual, unchanged)]);
            }
            cleanup.AddRange([
                Load(output),
                Load(native),
                Op(CilOperation.StoreField, handle),
                Argument(argument),
                Load(output),
                Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(type)),
                Mark(unchanged),
            ]);
        }
        if (isIn && kind == HandleKind.Safe)
        {
            var released = new CilBodyLabel();
            cleanup.AddRange([
                Load(added),
                Branch(CilOperation.BranchIfFalse, released),
                Load(input),
                Call(members.SafeHandleRelease),
                Mark(released),
            ]);
        }
        // The locals in the order the stub has always declared them.
        var declared = new List<CilBodyLocal>();
        if (isIn)
        {
            declared.AddRange([input, added]);
        }
        if (isOut)
        {
            declared.AddRange([output, original, marshalled]);
        }
        declared.Add(native);
        return new([.. declared], [.. marshal], [Address(native)], [.. unmarshal], [.. cleanup]);
    }
}
