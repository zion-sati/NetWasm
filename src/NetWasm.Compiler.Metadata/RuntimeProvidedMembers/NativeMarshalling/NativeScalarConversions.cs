// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// The CIL that converts a bool or char between its managed and native forms, applied to the value on the stack, as
// desktop .NET's IL stub converts it: a bool to 1 or 0 and a native value to true when it is not zero; an ANSI char
// through StubHelpers with the import's best-fit and unmappable-character settings; a UTF-16 char not at all.
internal sealed record NativeScalarConversions(NativeMarshallingMembers Members, bool BestFit, bool ThrowOnUnmappableChar)
{
    public ImmutableArray<CilBodyElement> ToNative(NativeScalarForm form) => form.Conversion switch
    {
        NativeScalarConversion.Boolean => NotZero(),
        NativeScalarConversion.AnsiChar =>
        [
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(BestFit ? 1 : 0)),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(ThrowOnUnmappableChar ? 1 : 0)),
            Call(Members.AnsiCharToNative),
        ],
        _ => [],
    };

    public ImmutableArray<CilBodyElement> ToManaged(NativeScalarForm form) => form.Conversion switch
    {
        NativeScalarConversion.Boolean => NotZero(),
        NativeScalarConversion.AnsiChar => [Call(Members.AnsiCharToManaged)],
        _ => [],
    };

    // 1 when the value is not zero, and 0 when it is.
    private static ImmutableArray<CilBodyElement> NotZero() =>
        [Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), Op(CilOperation.CompareGreaterThanUnsigned)];
}
