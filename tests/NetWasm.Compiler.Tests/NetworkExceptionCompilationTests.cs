using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NetworkExceptionCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ExplicitErrorDataAndDeferredHostLookups(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "NetworkExceptionFixture", Source, "NetworkExceptionFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    internal const string Source = """
        using System;
        using System.ComponentModel;
        using System.Net.Sockets;
        namespace NetworkExceptionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 1;
                var error = new SocketException((int)SocketError.InvalidArgument);
                if (error.NativeErrorCode != (int)SocketError.InvalidArgument || error.ErrorCode != error.NativeErrorCode || !error.Message.Contains("InvalidArgument")) return 2;
                // Upstream's constructors: the last P/Invoke error, and the C library's message for a code.
                System.Runtime.InteropServices.Marshal.SetLastPInvokeError(7);
                if (new Win32Exception().NativeErrorCode != 7) return 3;
                var coded = new Win32Exception(5);
                if (coded.NativeErrorCode != 5 || string.IsNullOrEmpty(coded.Message)) return 4;
                var plain = new Win32Exception("message");
                if (plain.NativeErrorCode != 7 || plain.Message != "message") return 5;
                var inner = new Exception();
                var wrapped = new Win32Exception("message", inner);
                if (wrapped.NativeErrorCode != 7 || wrapped.InnerException != inner || wrapped.Message != "message") return 6;
                try { _ = new SocketException(); return 7; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("SocketException")) return 8; }
                return input + 1;
            }
            public static int Portable(int input)
            {
                var error = new Win32Exception(123, "supplied");
                if (error.NativeErrorCode != 123 || error.Message != "supplied" || error.ErrorCode != unchecked((int)0x80004005)) return 10;
                if (!error.ToString().Contains("(123): supplied")) return 11;
                var negative = new Win32Exception(-1, "negative");
                if (!negative.ToString().Contains("(0xFFFFFFFF): negative")) return 12;
                var socket = new SocketException((int)SocketError.InvalidArgument, "provided");
                if (socket.SocketErrorCode != SocketError.InvalidArgument || socket.Message != "provided") return 13;
                try { throw socket; }
                catch (Win32Exception ex) { if (!ReferenceEquals(ex, socket)) return 14; }
                return input + 1;
            }
        }
        """;
}
