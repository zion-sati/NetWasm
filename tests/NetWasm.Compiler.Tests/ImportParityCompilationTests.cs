// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// VerifyImportParity analyzes an O0 program at O1 as well and fails when the two reach different host imports, naming
// the import and how it was reached. A native import behind a call only O0's dispatch by type reaches (a Native
// object is allocated but never reaches the Shape call) is reached at O0 alone.
public sealed class ImportParityCompilationTests
{
    private const string Source = """
        using System.Runtime.InteropServices;

        public abstract class Shape { public abstract int Area(); }
        public sealed class Square : Shape { public override int Area() => 4; }
        public sealed class Native : Shape
        {
            public override int Area() => Add(1, 2);

            [DllImport("native_math", EntryPoint = "add")]
            private static extern int Add(int a, int b);
        }
        public static class EntryPoint
        {
            private static object Kept = new Native();

            public static int Run(int input) => Measure(new Square()) + (Kept is null ? 1 : 0) + input;

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static int Measure(Shape shape) => shape.Area();
        }
        """;

    [Fact]
    public void AnImportOnlyOneLevelReachesFailsNamingHowItWasReached()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ImportParity", Source);
        var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O0)
        { VerifyImportParity = true };

        var failure = Assert.ThrowsAny<Exception>(() => NetWasmCompiler.Compile(options));

        var message = failure.ToString();
        Assert.Contains("host imports differ between optimization levels", message, StringComparison.Ordinal);
        Assert.Contains("only this level reaches", message, StringComparison.Ordinal);
        Assert.Contains("Native", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProgramReachingTheSameImportsAtBothLevelsCompiles()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ImportParity",
            Source.Replace("private static object Kept = new Native();", "private static object Kept = new Square();",
                StringComparison.Ordinal));
        var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O0)
        { VerifyImportParity = true };

        Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
    }

    // A build without a world that reaches a WIT import fails NW1009 naming how the import was reached.
    [Fact]
    public void AWitImportWithoutAWorldNamesHowItWasReached()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ImportParity", """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    System.Console.WriteLine(input);
                    return input;
                }
            }
            """);
        var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", []);

        var failure = Assert.Throws<NetWasm.Compiler.Core.CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Contains("NW1009", failure.Message, StringComparison.Ordinal);
        Assert.Contains("[required through ", failure.Message, StringComparison.Ordinal);
        Assert.Contains("EntryPoint", failure.Message, StringComparison.Ordinal);
    }
}
