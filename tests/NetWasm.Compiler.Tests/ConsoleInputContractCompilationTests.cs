using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConsoleInputContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmptyStandardInputEndsAndCallerSuppliedReadersReplaceIt(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ConsoleInputFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ConsoleInputFixture.EntryPoint",
            "Run", [new("run", "ConsoleInputFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.IO;
        namespace ConsoleInputFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // The oracle host's standard input is empty.
                if (Console.In is null || Console.Read() != -1 || Console.ReadLine() != null) return 1;
                var caught = 0;
                try { Console.SetIn(null); } catch (ArgumentNullException e) { if (e.ParamName == "newIn") caught++; }
                if (caught != 1) return 1;
                Console.SetIn(new StringReader("ab\r\ncd\nef"));
                if (Console.Read() != 'a' || Console.ReadLine() != "b" || Console.In.ReadLine() != "cd" || Console.ReadLine() != "ef") return 2;
                if (Console.Read() != -1 || Console.ReadLine() != null) return 3;
                Console.SetIn(new StringReader("replacement"));
                if (Console.ReadLine() != "replacement") return 4;
                try { Console.SetIn(null); } catch (ArgumentNullException) { caught++; }
                if (Console.Read() != -1) return 5;
                Console.SetIn(TextReader.Null);
                return caught == 2 ? input + 1 : 6;
            }
        }
        """;
}
