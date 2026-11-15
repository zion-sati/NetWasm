// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeQueryMethodSpecializer(
    IMethodSpecializer inner,
    IAttributeQueryLowerer queries) : IMethodSpecializer
{
    public CilMethodBody Rewrite(CilMethodBody body) => queries.Lower(inner.Rewrite(body));
}
