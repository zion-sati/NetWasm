using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConsoleTerminalContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnavailableTerminalOperationsRejectAtRuntime(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ConsoleTerminalFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ConsoleTerminalFixture.EntryPoint",
            "Run", [new("run", "ConsoleTerminalFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RedirectedOutputCompilesWithStandardImports(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ConsoleTerminalFixture", OutputSource);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ConsoleTerminalFixture.EntryPoint",
            "Run", [new("run", "ConsoleTerminalFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        // Console's platform stream closure is executed by the linked SDK probe.
        // This raw-module test verifies compilation and Wasm validation only.
    }

    private const string Source = """
        using System;
        using System.IO;
        namespace ConsoleTerminalFixture;
        public static class EntryPoint
        {
         public static int Run(int input)
         {
          var caught = 0;
          try { var value = Console.BackgroundColor; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.BackgroundColor = ConsoleColor.White; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.ForegroundColor; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.ForegroundColor = ConsoleColor.White; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.BufferHeight; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.BufferHeight = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.BufferWidth; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.BufferWidth = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.CursorLeft; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.CursorLeft = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.CursorTop; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.CursorTop = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.CursorSize; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.CursorSize = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.CursorVisible; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.CursorVisible = true; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.Title; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.Title = "title"; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.TreatControlCAsInput; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.TreatControlCAsInput = true; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.WindowHeight; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.WindowHeight = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.WindowWidth; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.WindowWidth = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.WindowLeft; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.WindowLeft = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.WindowTop; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.WindowTop = 1; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.CapsLock; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.NumberLock; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.KeyAvailable; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.LargestWindowHeight; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var value = Console.LargestWindowWidth; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.Beep(); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.Beep(800, 1); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.Clear(); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.ResetColor(); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.SetBufferSize(80, 25); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.SetCursorPosition(0, 0); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.SetWindowPosition(0, 0); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.SetWindowSize(80, 25); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.MoveBufferArea(0, 0, 1, 1, 1, 1); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.MoveBufferArea(0, 0, 1, 1, 1, 1, ' ', ConsoleColor.White, ConsoleColor.Black); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.ReadKey(); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.ReadKey(true); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.CancelKeyPress += Handler; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Console.CancelKeyPress -= Handler; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          return caught == 47 ? input + 1 : 2;
         }
         private static void Handler(object sender, ConsoleCancelEventArgs args) { }
        }
        """;
    private const string OutputSource = """
        using System;
        using System.IO;
        namespace ConsoleTerminalFixture;
        public static class EntryPoint
        {
         public static int Run(int input)
         {
          var writer = new StringWriter();
          Console.SetOut(writer);
          Console.Write(42);
          Console.SetOut(TextWriter.Null);
          if (writer.ToString() != "42") return 1;
          return input + 1;
         }
        }
        """;

}
