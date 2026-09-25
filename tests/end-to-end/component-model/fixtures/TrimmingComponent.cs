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
