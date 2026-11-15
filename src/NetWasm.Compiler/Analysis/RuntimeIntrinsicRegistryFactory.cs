// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class RuntimeIntrinsicRegistryFactory : IRuntimeIntrinsicRegistryFactory
{
    public IRuntimeIntrinsicRegistry Create(
        ISymbolFormatter symbols,
        IEnumerable<MethodDefinitionModel> methods) =>
        new RuntimeIntrinsicRegistry(symbols, methods);
}
