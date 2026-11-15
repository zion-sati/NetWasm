// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class CalledMethodResolverFactory : ICalledMethodResolverFactory
{
    public ICalledMethodResolver Create(
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols) =>
        new CalledMethodResolver(methods, methodInstances, symbols);
}
