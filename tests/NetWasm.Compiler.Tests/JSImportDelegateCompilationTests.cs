using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class JSImportDelegateCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DelegateBoundToAJSImportMethodGroupProducesValidWasm(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "JSImportMethodGroupFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class Program
            {
                public static int Run(int input)
                {
                    Func<string, string?> resolve = Host.Resolve;
                    Func<string, string?> lambda = name => Host.Resolve(name);
                    Host.Report(resolve("x") ?? lambda("y") ?? "<null>");
                    return 0;
                }
            }

            internal static class Host
            {
                [JSImport("resolve", "repro.host")]
                internal static extern string? Resolve(string name);

                [JSImport("report", "repro.host")]
                internal static extern void Report(string value);
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Program",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(["report", "resolve"],
            result.InteropManifest.Imports.Select(import => import.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DelegateBoundToACallbackRegisteringJSImportIsRejected()
    {
        var exception = CompileRejected(
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class Program
            {
                public static int Run(int input)
                {
                    Func<Func<int, int>, JSSubscription> register = Host.Register;
                    using var subscription = register(value => value * 2);
                    return 0;
                }
            }

            internal static class Host
            {
                [JSImport("register", "repro.host")]
                internal static extern JSSubscription Register(Func<int, int> callback);
            }
            """);

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("registers a JavaScript callback", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateBoundToAnAsynchronousJSImportIsRejected()
    {
        var exception = CompileRejected(
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;
            using System.Threading.Tasks;

            public static class Program
            {
                public static int Run(int input)
                {
                    Func<Task<int>> fetch = Host.FetchAsync;
                    return fetch().IsCompleted ? 1 : 0;
                }
            }

            internal static class Host
            {
                [JSImport("fetch", "repro.host")]
                internal static extern Task<int> FetchAsync();
            }
            """);

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("returns a Task or ValueTask", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    // A forwarder has no value frame for a date result; a date argument is its address.
    [Fact]
    public void DelegateBoundToAJSImportReturningADateIsRejected()
    {
        var exception = CompileRejected(
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class Program
            {
                public static int Run(int input)
                {
                    Func<DateTime> now = Host.Now;
                    return now().Year;
                }
            }

            internal static class Host
            {
                [JSImport("now", "repro.host")]
                internal static extern DateTime Now();
            }
            """);

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("returns a DateTime, DateTimeOffset or nullable value", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DelegateBoundToAJSImportTakingADateProducesValidWasm(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "JSImportDateDelegateFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class Program
            {
                public static int Run(int input)
                {
                    Func<DateTime, int> day = Host.Day;
                    return day(new DateTime(2000, 1, input + 1));
                }
            }

            internal static class Host
            {
                [JSImport("day", "repro.host")]
                internal static extern int Day(DateTime value);
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Program",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(["date"], Assert.Single(result.InteropManifest.Imports).Parameters.ToArray());
    }

    private static CompilerException CompileRejected(string source)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("JSImportDelegateRejectionFixture", source);

        return Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Program",
            "Run",
            [])));
    }
}
