// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.StackTraces;

internal interface IStackTraceReachabilityRootProvider
{
    ReachabilityRoots Provide(
        bool stackTraces,
        ITypeFinder types,
        IFieldRepository fields,
        IMethodRepository methods);
}
