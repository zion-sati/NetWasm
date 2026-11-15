// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

internal static class RandomCilDiagnosticReplaySelector
{
    public static int SelectCase(int input, int inputCount, int caseCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(caseCount);

        if (input >= inputCount || inputCount % caseCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        return input / (inputCount / caseCount);
    }
}
