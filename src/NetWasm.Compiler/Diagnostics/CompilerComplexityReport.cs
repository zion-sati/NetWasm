// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Diagnostics;

public sealed record CompilerComplexityReport(
    ImmutableArray<CompilerMethodComplexityMetric> Methods,
    int TotalCilInstructions,
    int TotalOriginalBlocks,
    int TotalCfgEdges,
    int TotalWasmInstructions,
    int TotalWasmBodyBytes);
