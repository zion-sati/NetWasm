// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Correctness.MultiFile;

public static partial class EntryPoint
{
    private static int Right(int input) => new LocalValue().Read(input);
}

file sealed class LocalValue
{
    public int Read(int input) => 19 - input;
}
