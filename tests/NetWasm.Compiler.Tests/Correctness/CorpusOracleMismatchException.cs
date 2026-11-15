// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CorpusOracleMismatchException(int input, string message)
    : InvalidOperationException(message)
{
    public int Input { get; } = input;
}
