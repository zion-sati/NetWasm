using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnvironmentPlatformContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnavailableEnvironmentOperationsRejectAtRuntime(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EnvironmentPlatformFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EnvironmentPlatformFixture.EntryPoint",
            "Run", [new("run", "EnvironmentPlatformFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace EnvironmentPlatformFixture;
        public static class EntryPoint
        {
         public static int Run(int input)
         {
          var caught = 0;
          try { var v = Environment.UserInteractive; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var v = Environment.SystemPageSize; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var v = Environment.WorkingSet; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var v = Environment.CommandLine; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var v = Environment.StackTrace; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var v = Environment.SystemDirectory; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var v = Environment.CurrentDirectory; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { var v = Environment.ExitCode; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Environment.CurrentDirectory = "."; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Environment.GetLogicalDrives(); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Environment.ExitCode = 42; } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Environment.Exit(42); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Environment.FailFast("failure"); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          try { Environment.FailFast("failure", new Exception()); } catch (PlatformNotSupportedException e) { if (e.Message.Length != 0) caught++; }
          if (Environment.ProcessorCount != 1 || Environment.CurrentManagedThreadId != 1 || Environment.HasShutdownStarted) return 1;
          // Environment.Version reports NetWasm's own version; RuntimeIdentityCompilationTests checks its value.
          if (Environment.Version.Major < 0 || Environment.Version.Revision != -1) return 3;
          // Upstream's Browser and WASI builds report an "Other" platform at 1.0.0.0.
          if (Environment.OSVersion.Platform != PlatformID.Other || Environment.OSVersion.Version != new Version(1, 0, 0, 0)) return 4;
          return caught == 14 ? input + 1 : 2;
         }
        }
        """;
}
