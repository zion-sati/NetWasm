// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// An export gives a JSException's JavaScript error back to JavaScript only where the program builds a JSException
// holding one: a failed import, or an Error crossing as a value. A program whose exports pass only numbers reaches
// none of the error or string interop, and one with both roots the offer whichever the closure reaches first.
public sealed class JavaScriptExportFailureRootingTests
{
    private const string ExportsOnly = """
        using System.Runtime.InteropServices.JavaScript;
        using System.Threading.Tasks;

        public static class EntryPoint
        {
            public static int Run(int input) => input;

            [JSExport("add")]
            public static int Add(int value) => value + 1;

            [JSExport("later")]
            public static async Task<int> Later(int value)
            {
                await Task.Yield();
                return value + 2;
            }
        }
        """;

    // The import is reached from the entry point, before any export's body.
    private const string ImportFromTheEntry = """
        using System.Runtime.InteropServices.JavaScript;

        public static partial class EntryPoint
        {
            [JSImport("read", "test.host")]
            private static extern int Read(int value);

            public static int Run(int input) => Read(input);

            [JSExport("add")]
            public static int Add(int value) => value + 1;
        }
        """;

    // The import is reached only from an async export's body, after the export boundary.
    private const string ImportFromAnExport = """
        using System.Runtime.InteropServices.JavaScript;
        using System.Threading.Tasks;

        public static partial class EntryPoint
        {
            [JSImport("read", "test.host")]
            private static extern Task<int> Read(int value);

            public static int Run(int input) => input;

            [JSExport("later")]
            public static async Task<int> Later(int value) => await Read(value);
        }
        """;

    // No import: an Error crossing into an export as an exception builds the JSException.
    private const string ErrorAsAValue = """
        using System;
        using System.Runtime.InteropServices.JavaScript;

        public static class EntryPoint
        {
            public static int Run(int input) => input;

            [JSExport("rethrow")]
            public static int Rethrow(Exception error) => throw error;
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ExportsThatPassOnlyNumbersReachNoErrorOrStringInterop(FinalWasmOptimization optimization)
    {
        var result = Compile(ExportsOnly, optimization);

        Assert.Empty(result.InteropManifest.Imports);
        Assert.False(result.Program.ProducesJavaScriptErrors);
        Assert.False(result.Program.JavaScriptConversions.ContainsKey(JavaScriptConversion.ExportFailure));
    }

    [Theory]
    [InlineData(nameof(ImportFromTheEntry))]
    [InlineData(nameof(ImportFromAnExport))]
    [InlineData(nameof(ErrorAsAValue))]
    public void AProgramThatBuildsJavaScriptErrorsRootsTheOfferWhicheverItReachesFirst(string program)
    {
        var source = program switch
        {
            nameof(ImportFromTheEntry) => ImportFromTheEntry,
            nameof(ImportFromAnExport) => ImportFromAnExport,
            _ => ErrorAsAValue,
        };

        var result = Compile(source, FinalWasmOptimization.Oz);

        Assert.True(result.Program.ProducesJavaScriptErrors);
        Assert.True(result.Program.JavaScriptConversions.ContainsKey(JavaScriptConversion.ExportFailure));
    }

    private static CompilationResult Compile(string source, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ExportFailureRootingFixture", source);
        return NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));
    }
}
