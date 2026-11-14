using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SocketHostContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    // SendPacketsElement and the control codes are portable data contracts. Sockets themselves run
    // over the platform's network, which ManagedExecutableAsyncMainCompilationTests covers in
    // Wasmtime.
    public void SendPacketsElementsAndControlCodesArePortable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SocketHostFixture", Source, "SocketHostFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Net.Sockets;
        namespace SocketHostFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var buffer = new byte[] { 1, 2, 3 };
                var all = new SendPacketsElement(buffer);
                if (!ReferenceEquals(all.Buffer, buffer) || all.Count != 3 || all.Offset != 0 || all.EndOfPacket || all.FilePath != null) return 12;
                var slice = new SendPacketsElement(buffer, 1, 2, true);
                if (slice.Count != 2 || slice.Offset != 1 || !slice.EndOfPacket || !ReferenceEquals(slice.Buffer, buffer)) return 13;
                var empty = new SendPacketsElement(buffer, 3, 0);
                if (empty.Count != 0 || empty.Offset != 3) return 14;
                var file = new SendPacketsElement("file.bin", 5, 7, true);
                if (file.FilePath != "file.bin" || file.Offset != 5 || file.Count != 7 || !file.EndOfPacket || file.Buffer != null) return 15;
                if (new SendPacketsElement("file.bin").Count != 0) return 16;
                var errors = 0;
                try { _ = new SendPacketsElement((byte[])null); return 17; } catch (ArgumentNullException) { errors++; }
                try { _ = new SendPacketsElement(buffer, -1, 1); return 18; } catch (ArgumentOutOfRangeException) { errors++; }
                try { _ = new SendPacketsElement(buffer, 1, 3); return 19; } catch (ArgumentOutOfRangeException) { errors++; }
                try { _ = new SendPacketsElement((string)null); return 20; } catch (ArgumentNullException) { errors++; }
                try { _ = new SendPacketsElement("file.bin", -1, 0); return 21; } catch (ArgumentOutOfRangeException) { errors++; }
                try { _ = new SendPacketsElement("file.bin", 0, -1); return 22; } catch (ArgumentOutOfRangeException) { errors++; }
                if ((long)IOControlCode.NonBlockingIO != 0x8004667eL || (long)IOControlCode.DataToRead != 0x4004667fL) return 23;
                return errors == 6 ? input + 1 : 24;
            }
        }
        """;
}
