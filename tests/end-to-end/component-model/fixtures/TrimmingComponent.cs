// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Fixtures.ComponentModel;

public static class PureTrimmingComponent
{
    public static int Run(int input) => checked(input + 1);
}

public static class ClockTrimmingComponent
{
    public static int Run(int input) =>
        checked((int)(DateTime.UtcNow.Ticks % 1_000) + input);
}
