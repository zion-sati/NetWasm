using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConsolePlatformContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConsolePlatformMembersFollowTheStandardStreams(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ConsolePlatformFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ConsolePlatformFixture.EntryPoint",
            "Run", [new("run", "ConsolePlatformFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    // The oracle host has no terminal and an empty standard input, so every standard stream is
    // redirected and input ends at once. Console's encodings are fixed at UTF-8 on NetWasm.
    private const string Source = """
        using System;
        namespace ConsolePlatformFixture;
        public static class EntryPoint
        {
         public static int Run(int input)
         {
          if (!Console.IsInputRedirected || !Console.IsOutputRedirected || !Console.IsErrorRedirected) return 1;
          using (var stdin = Console.OpenStandardInput())
          {
           if (!stdin.CanRead || stdin.CanWrite || stdin.ReadByte() != -1) return 2;
          }
          using (var stdin = Console.OpenStandardInput(0)) { if (stdin.ReadByte() != -1) return 3; }
          using (var stdout = Console.OpenStandardOutput(0))
          {
           if (!stdout.CanWrite || stdout.CanRead) return 4;
           stdout.Write(new[] { (byte)'o', (byte)'k' }, 0, 2);
          }
          using (var stdout = Console.OpenStandardOutput()) { if (!stdout.CanWrite) return 5; }
          using (var stderr = Console.OpenStandardError()) { if (!stderr.CanWrite || stderr.CanRead) return 6; }
          using (var stderr = Console.OpenStandardError(0)) { if (!stderr.CanWrite) return 7; }
          var caught = 0;
          try { var v = Console.InputEncoding; } catch (PlatformNotSupportedException) { caught++; }
          try { Console.InputEncoding = System.Text.Encoding.UTF8; } catch (PlatformNotSupportedException) { caught++; }
          try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (PlatformNotSupportedException) { caught++; }
          if (caught != 3 || Console.OutputEncoding.CodePage != 65001) return 8;
          try { Console.InputEncoding = null; } catch (ArgumentNullException e) { if (e.ParamName == "value") caught++; }
          try { Console.OutputEncoding = null; } catch (ArgumentNullException e) { if (e.ParamName == "value") caught++; }
          try { Console.OpenStandardInput(-1); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "bufferSize") caught++; }
          try { Console.OpenStandardOutput(-1); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "bufferSize") caught++; }
          try { Console.OpenStandardError(-1); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "bufferSize") caught++; }
          return caught == 8 ? input + 1 : 9;
         }
        }
        """;
}
