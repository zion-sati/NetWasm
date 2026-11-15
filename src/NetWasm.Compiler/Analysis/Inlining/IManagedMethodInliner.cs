// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal interface IManagedMethodInliner
{
    CilMethodBody Inline(
        MethodInstanceModel caller,
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks,
        bool nested = false,
        bool smallOnly = false);
}
