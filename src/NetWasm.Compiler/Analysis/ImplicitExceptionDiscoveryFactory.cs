// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ImplicitExceptionDiscoveryFactory : IImplicitExceptionDiscoveryFactory
{
    public IImplicitExceptionDiscovery Create(
        IMethodRepository methods,
        ISymbolFormatter symbols,
        IRuntimeIntrinsicRegistry intrinsics) =>
        new ImplicitExceptionDiscovery(
            methods,
            symbols,
            new RuntimeIntrinsicExceptionRequirementProvider(intrinsics));
}
