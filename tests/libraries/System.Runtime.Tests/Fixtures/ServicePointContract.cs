// From the compiler harness's ServicePointContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0014
using System;
using System.Net;
namespace ServicePointFixture;

public static class EntryPoint
{
    public static int Run(int value)
    {
        ServicePoint first = ServicePointManager.FindServicePoint(new Uri("http://example.test:8080/a"));
        ServicePoint again = ServicePointManager.FindServicePoint(new Uri("http://example.test:8080/b"));
        if (!ReferenceEquals(first, again)) return 1;
        if (first.Address.ToString() != "http://example.test:8080/a" || first.ConnectionName != "http") return 2;
        if (first.ConnectionLimit != ServicePointManager.DefaultConnectionLimit) return 3;
        if (first.CurrentConnections != 0 || !first.CloseConnectionGroup("group")) return 4;
        if (ReferenceEquals(first, ServicePointManager.FindServicePoint(new Uri("http://other.test/")))) return 5;

        try
        {
            first.ConnectionLimit = 0;
            return 6;
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3;
            return 7;
        }
        catch (NotSupportedException)
        {
        }
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        if (ServicePointManager.SecurityProtocol != SecurityProtocolType.Tls12) return 8;
        try
        {
            ServicePointManager.SetTcpKeepAlive(true, 0, 1);
            return 9;
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        ServicePointManager.SetTcpKeepAlive(false, 0, 0);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 80);
        first.BindIPEndPointDelegate = (servicePoint, remote, retryCount) => remote;
        if (!ReferenceEquals(first.BindIPEndPointDelegate(first, endPoint, 0), endPoint)) return 10;

        // SmtpClient's ServicePoint needs its host, and is then the one for that host and port.
        try
        {
            _ = new System.Net.Mail.SmtpClient().ServicePoint;
            return 11;
        }
        catch (InvalidOperationException)
        {
        }
        if (new System.Net.Mail.SmtpClient("smtp.example.test", 2525).ServicePoint.Address.Authority != "smtp.example.test:2525") return 12;

        return value + 1;
    }
}
