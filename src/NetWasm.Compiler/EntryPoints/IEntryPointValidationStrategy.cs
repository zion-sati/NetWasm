// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IEntryPointValidationStrategy
{
    CompilerEntryPointKind Kind { get; }

    void Validate(
        MethodDefinitionModel entryPoint,
        ISymbolFormatter symbols);
}
