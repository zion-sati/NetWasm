// From the compiler harness's ThreadSupportingContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
namespace ThreadSupportFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        if ((int)ApartmentState.STA != 0 || (int)ApartmentState.MTA != 1 || (int)ApartmentState.Unknown != 2) return 1;
        if ((int)ThreadPriority.Lowest != 0 || (int)ThreadPriority.BelowNormal != 1 || (int)ThreadPriority.Normal != 2 || (int)ThreadPriority.AboveNormal != 3 || (int)ThreadPriority.Highest != 4) return 2;
        var state = ThreadState.Background | ThreadState.Unstarted;
        if ((int)state != 12 || (state & ThreadState.Background) == 0) return 3;
        var calls = 0;
        ThreadStart first = () => calls++;
        ThreadStart second = () => calls += 2;
        var combined = first + second; combined();
        combined -= second; combined!();
        if (calls != 4) return 4;
        var marker = new object(); object? observed = null;
        ParameterizedThreadStart parameterized = value => observed = value;
        parameterized(marker); if (!ReferenceEquals(marker, observed)) return 5;
        parameterized(null); if (observed != null) return 6;
        return input + 1;
    }
}
