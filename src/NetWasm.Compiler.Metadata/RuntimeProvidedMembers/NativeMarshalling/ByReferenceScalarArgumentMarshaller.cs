// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A bool or char passed by reference, as desktop .NET passes it: the native function receives the address of a local
// holding the native form. In, the caller's value converts into it before the call; out, it converts back into the
// caller's variable after the call. An out-only parameter starts the local at zero, so the function reads no value.
internal static class ByReferenceScalarArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, CliTypeIdentity type, NativeScalarForm form, bool isIn, bool isOut,
        NativeScalarConversions conversions)
    {
        var native = new CilBodyLocal(form.Native);
        var managed = new CilOperand.TypeIdentity(type);
        var marshal = new List<CilBodyElement>();
        if (isIn)
        {
            marshal.AddRange([Argument(argument), Op(CilOperation.LoadObject, managed), .. conversions.ToNative(form)]);
        }
        else
        {
            marshal.Add(Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)));
        }
        marshal.Add(Store(native));
        CilBodyElement[] unmarshal = isOut
            ? [Argument(argument), Load(native), .. conversions.ToManaged(form), Op(CilOperation.StoreObject, managed)]
            : [];
        return new([native], [.. marshal], [Address(native)], [.. unmarshal], []);
    }
}
