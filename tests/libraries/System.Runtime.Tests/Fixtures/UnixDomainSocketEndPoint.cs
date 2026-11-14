// From the compiler harness's UnixDomainSocketEndPointCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Net;
using System.Net.Sockets;
namespace UnixDomainEndPointFixture;

public static class EntryPoint
{
    public static int Run(int value)
    {
        try
        {
            _ = new UnixDomainSocketEndPoint(null!);
            return 1;
        }
        catch (ArgumentNullException)
        {
        }

#if !DESKTOP_ORACLE
        try
        {
            EndPoint endPoint = new UnixDomainSocketEndPoint("/tmp/netwasm.sock");
            return 2;
        }
        catch (PlatformNotSupportedException)
        {
        }
#endif

        return value + 1;
    }
}
