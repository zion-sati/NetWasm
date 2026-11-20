// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// Lays a struct out natively for both pointer sizes from the value types the source describes, as NativeStructCopier
// copies it.
internal sealed class NativeStructCopierFactory(INativeStructSource structs) : INativeStructCopierFactory
{
    private readonly INativeStructSource _structs = structs ?? throw new ArgumentNullException(nameof(structs));

    // Whether a struct lays out natively, and whether it converts, does not depend on the pointer size; only its sizes
    // and offsets do.
    public INativeStructCopier? Create(CliTypeIdentity type, NativeScalarConversions conversions) =>
        NativeStructLayouts.Plan(type, _structs, pointerSize: 4) is { Converts: true } narrow
            ? new NativeStructCopier(type, narrow, NativeStructLayouts.Plan(type, _structs, pointerSize: 8)!, conversions)
            : null;
}
