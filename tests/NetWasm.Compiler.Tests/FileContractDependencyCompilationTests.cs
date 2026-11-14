using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FileContractDependencyCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OptionsAndHandleOwnershipMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "FileDependencyFixture", Source, "FileDependencyFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.IO;
        using Microsoft.Win32.SafeHandles;
        namespace FileDependencyFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var options = new EnumerationOptions();
                if (!options.IgnoreInaccessible || options.RecurseSubdirectories || options.ReturnSpecialDirectories) return 1;
                if (options.AttributesToSkip != (FileAttributes.Hidden | FileAttributes.System) || options.BufferSize != 0 || options.MaxRecursionDepth != int.MaxValue) return 2;
                if (options.MatchCasing != MatchCasing.PlatformDefault || options.MatchType != MatchType.Simple) return 3;
                options.RecurseSubdirectories = true; options.IgnoreInaccessible = false; options.ReturnSpecialDirectories = true;
                options.AttributesToSkip = 0; options.BufferSize = -1; options.MatchCasing = MatchCasing.CaseInsensitive; options.MatchType = MatchType.Win32; options.MaxRecursionDepth = 0;
                if (!options.RecurseSubdirectories || options.IgnoreInaccessible || !options.ReturnSpecialDirectories || options.BufferSize != -1 || options.MaxRecursionDepth != 0) return 4;
                try { options.MaxRecursionDepth = -1; return 5; } catch (ArgumentOutOfRangeException) { }
                var zero = new BothInvalid(0, true); if (!zero.IsInvalid) return 6; zero.Dispose();
                var minus = new BothInvalid(-1, true); if (!minus.IsInvalid) return 7; minus.Dispose();
                var owned = new BothInvalid(42, true); if (owned.IsInvalid) return 8; owned.Dispose(); owned.Dispose();
                var borrowed = new BothInvalid(42, false); borrowed.Dispose();
                if (BothInvalid.Releases != 1 || !owned.IsClosed || !borrowed.IsClosed) return 9;
                var zeroAllowed = new MinusInvalid(0); if (zeroAllowed.IsInvalid) return 10; zeroAllowed.Dispose();
                var invalid = new MinusInvalid(-1); if (!invalid.IsInvalid) return 11; invalid.Dispose();
                return MinusInvalid.Releases == 1 ? input + 1 : 12;
            }
        }
        public sealed class BothInvalid : SafeHandleZeroOrMinusOneIsInvalid
        {
            public static int Releases;
            public BothInvalid(int value, bool owns) : base(owns) { SetHandle(new IntPtr(value)); }
            protected override bool ReleaseHandle() { Releases++; return true; }
        }
        public sealed class MinusInvalid : SafeHandleMinusOneIsInvalid
        {
            public static int Releases;
            public MinusInvalid(int value) : base(true) { SetHandle(new IntPtr(value)); }
            protected override bool ReleaseHandle() { Releases++; return true; }
        }
        """;
}
