using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NetworkingEnumContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NetworkingValuesAndFlagsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "NetworkingEnumFixture", Source, "NetworkingEnumFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Net;
        using System.Net.Sockets;
        namespace NetworkingEnumFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if ((int)AddressFamily.InterNetwork != 2 || (int)AddressFamily.InterNetworkV6 != 23) return 1;
                if ((int)SocketType.Stream != 1 || (int)SocketType.Dgram != 2 || (int)SocketType.Unknown != -1) return 2;
                if ((int)ProtocolType.Tcp != 6 || (int)ProtocolType.Udp != 17) return 3;
                if ((int)SocketError.Success != 0 || (int)SocketError.SocketError != -1) return 4;
                var socketFlags = SocketFlags.Peek | SocketFlags.DontRoute;
                if (!socketFlags.HasFlag(SocketFlags.Peek) || !socketFlags.HasFlag(SocketFlags.DontRoute) || socketFlags.HasFlag(SocketFlags.OutOfBand)) return 5;
                var compression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (!compression.HasFlag(DecompressionMethods.GZip) || (int)compression != 3) return 6;
                var auth = AuthenticationSchemes.Basic | AuthenticationSchemes.Digest;
                if (!auth.HasFlag(AuthenticationSchemes.Basic) || auth.HasFlag(AuthenticationSchemes.Anonymous)) return 7;
                if ((int)SelectMode.SelectRead != 0 || (int)SocketShutdown.Both != 2) return 8;
                if ((int)SocketOptionLevel.Tcp != 6 || (int)SocketOptionName.ReuseAddress != 4) return 9;
                if ((int)SocketAsyncOperation.None != 0 || (int)IPProtectionLevel.Unspecified != -1) return 10;
                var transmit = TransmitFileOptions.Disconnect | TransmitFileOptions.ReuseSocket;
                if (!transmit.HasFlag(TransmitFileOptions.Disconnect)) return 11;
                if (HttpRequestHeader.Authorization.ToString() != "Authorization" || HttpResponseHeader.ContentType.ToString() != "ContentType") return 12;
                if (WebExceptionStatus.Timeout.ToString() != "Timeout" || SecurityProtocolType.Tls12.ToString() != "Tls12") return 13;
                return input + 1;
            }
        }
        """;
}
