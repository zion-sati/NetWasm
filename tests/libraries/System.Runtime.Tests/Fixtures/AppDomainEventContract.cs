// From the compiler harness's AppDomainEventContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Runtime.ExceptionServices;
namespace DomainEventFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        if (Portable(input) != 42) return 20;
        var domain = AppDomain.CurrentDomain; var calls = 0; var rejected = 0;
        EventHandler basic = (s,e) => calls++;
        UnhandledExceptionEventHandler unhandled = (s,e) => calls++;
        EventHandler<FirstChanceExceptionEventArgs> first = (s,e) => calls++;
        // Subscribing works as upstream's accessors do; none of these is raised during the program.
        domain.DomainUnload += basic;
        domain.DomainUnload -= basic;
        domain.ProcessExit += basic;
        domain.ProcessExit -= basic;
        domain.UnhandledException += unhandled;
        domain.UnhandledException -= unhandled;
        domain.FirstChanceException += first;
        domain.FirstChanceException -= first;
        domain.UnhandledException += unhandled;
        domain.FirstChanceException += first;
        domain.UnhandledException -= unhandled;
        domain.FirstChanceException -= first;
        if (calls != 0 || rejected != 0) return 21;
        return input + 1;
    }
    public static int Portable(int input)
    {
        var marker = new object(); var exception = new InvalidOperationException("marker");
        var unhandled = new UnhandledExceptionEventArgs(marker, true);
        if (!ReferenceEquals(unhandled.ExceptionObject, marker) || !unhandled.IsTerminating) return 1;
        var empty = new UnhandledExceptionEventArgs(null!, false);
        if (empty.ExceptionObject != null || empty.IsTerminating) return 2;
        var first = new FirstChanceExceptionEventArgs(exception);
        if (!ReferenceEquals(first.Exception, exception) || new FirstChanceExceptionEventArgs(null!).Exception != null) return 3;
        var calls = 0;
        UnhandledExceptionEventHandler a = (sender, e) => { if (ReferenceEquals(sender, marker) && ReferenceEquals(e, unhandled)) calls++; };
        UnhandledExceptionEventHandler b = (sender, e) => calls += 2;
        var both = a + b; both(marker, unhandled); both -= b; both!(marker, unhandled);
        if (calls != 4) return 4;
        EventHandler<FirstChanceExceptionEventArgs> handler = (sender,e) => { if (ReferenceEquals(e.Exception, exception)) calls++; };
        handler(marker, first);
        if (calls != 5) return 5;
        return input + 1;
    }
}
