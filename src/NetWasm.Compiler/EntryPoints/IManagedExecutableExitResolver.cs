// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IManagedExecutableExitResolver
{
    // CoreLib's process-exit method, which a managed executable's entry calls after Main
    // returns, with Main's result when it is an int; null for any other entry.
    EntityKey? Resolve(
        CompilerEntryPointKind kind,
        CliValueKind entryResult,
        ITypeFinder types,
        IMethodRepository methods);
}
