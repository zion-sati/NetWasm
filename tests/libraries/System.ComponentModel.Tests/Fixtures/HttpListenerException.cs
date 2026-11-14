// From the compiler harness's HttpListenerExceptionCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using System.Net;
namespace HttpListenerExceptionFixture;

public static class EntryPoint
{
    public static int Run(int value)
    {
        var exception = new HttpListenerException(5, "denied");
        if (exception.ErrorCode != 5 || exception.NativeErrorCode != 5 || exception.Message != "denied") return 1;
        if (exception.HResult == 5) return 2;
        Exception thrown = exception;
        if (thrown is not Win32Exception) return 3;

        // Upstream's: the last P/Invoke error, and the C library's message for a code.
        if (new HttpListenerException().ErrorCode != System.Runtime.InteropServices.Marshal.GetLastPInvokeError()) return 4;
        var coded = new HttpListenerException(5);
        if (coded.ErrorCode != 5 || string.IsNullOrEmpty(coded.Message)) return 5;

        return value + 1;
    }
}
