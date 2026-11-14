using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StringWriterPayForPlayCompilationTests
{
    private static readonly string[] CultureMachinery =
    [
        "System.Globalization.CultureInfo",
        "System.Globalization.CultureData",
        "System.Globalization.DateTimeFormatInfo",
        "System.Globalization.Calendar",
        "System.Globalization.GregorianCalendar",
    ];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DefaultProviderStringWriterDoesNotReachCultureMachinery(bool optimize, WasmTarget target)
    {
        // A StringWriter created with the default provider captures the current culture from CultureInfo's
        // mirror, so text-only use does not make CultureInfo reachable.
        const string source = """
            using System.IO;
            using System.Text;
            namespace StringWriterTextFixture;
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var writer = new StringWriter();
                    writer.Write("a");
                    writer.Write('b');
                    writer.Write(new[] { 'c' }, 0, 1);
                    var builder = new StringBuilder("d");
                    new StringWriter(builder).Write("e");
                    return writer.ToString() == "abc" && builder.ToString() == "de" ? value + 1 : 0;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var result = Compile(assets, "StringWriterTextFixture", source, optimize, target);
        var reachable = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(reachable, CultureMachinery.Contains);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DefaultProviderStringWriterCapturesTheCultureCurrentAtConstruction(bool optimize, WasmTarget target)
    {
        // As upstream, the default provider is the culture current when the writer was created, whether that is a
        // customized culture or the invariant one; an explicit null provider follows the culture current at write time.
        const string source = """
            using System;
            using System.Globalization;
            using System.IO;
            namespace StringWriterCultureFixture;
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                    custom.NumberFormat.NumberDecimalSeparator = ",";

                    var invariantWriter = new StringWriter();
                    CultureInfo.CurrentCulture = custom;
                    var customWriter = new StringWriter();
                    var nullProviderWriter = new StringWriter((IFormatProvider?)null);
                    nullProviderWriter.Write(1.5);
                    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

                    invariantWriter.Write(2.5);
                    customWriter.Write(3.5);
                    nullProviderWriter.Write(4.5);
                    if (invariantWriter.ToString() != "2.5" || customWriter.ToString() != "3,5" ||
                        nullProviderWriter.ToString() != "1,54.5") return 1;
                    if (!ReferenceEquals(invariantWriter.FormatProvider, CultureInfo.InvariantCulture) ||
                        !ReferenceEquals(customWriter.FormatProvider, custom)) return 2;
                    return value + 1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var result = Compile(assets, "StringWriterCultureFixture", source, optimize, target);

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    private static CompilationResult Compile(TestAssets assets, string name, string source, bool optimize, WasmTarget target)
    {
        var assembly = optimize ? assets.CompileOptimizedSource(name, source) : assets.CompileSource(name, source);
        return NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], $"{name}.EntryPoint", "Run",
            [new("run", $"{name}.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
    }
}
