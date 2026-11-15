// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Runtime.InteropServices.WebAssembly;

namespace NetWasm.Acceptance.Components;

public static class ScalarComponent
{
    [WitImport("increment")]
    private static extern int Increment(int value);

    [WitExport("run")]
    public static int Run(int value) => Increment(value);
}
