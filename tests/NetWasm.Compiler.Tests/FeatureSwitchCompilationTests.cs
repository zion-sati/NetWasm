using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A project's Trim="true" feature switch folds its [FeatureSwitchDefinition] getter to the project's value, as ILLink
// substitutes it, so the code the switch guards leaves the program; without the option the getter runs.
public sealed class FeatureSwitchCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void AFoldedSwitchRemovesTheCodeItGuards(bool enabled, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("FeatureSwitchFixture", Source);

        var result = NetWasmCompiler.Compile(Options(assets, assembly, target) with
        {
            FeatureSwitches = ImmutableDictionary<string, bool>.Empty.Add(Switch, enabled),
        });

        Assert.Equal(enabled, Reaches(result, "Expensive"));
        Assert.Equal(enabled ? 124 : 42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Fact]
    public void WithoutTheOptionTheGetterRuns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("FeatureSwitchFixture", Source);

        var result = NetWasmCompiler.Compile(Options(assets, assembly, WasmTarget.Wasm32));

        Assert.True(Reaches(result, "Expensive"));
        Assert.Equal(124, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // AppContext reads the project's RuntimeHostConfigurationOption items as .NET reads runtimeconfig.json's properties;
    // an app's own SetData and SetSwitch still come first, and an unset name has no value.
    [Fact]
    public void AppContextReadsTheProjectsRuntimeProperties()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RuntimePropertiesFixture", """
            using System;
            namespace RuntimePropertiesFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (AppContext.GetData("Example.Text") as string != "a=b c") return 1;
                    if (!AppContext.TryGetSwitch("Example.Flag", out var flag) || !flag) return 2;
                    if (!AppContext.TryGetSwitch("Example.Off", out var off) || off) return 3;
                    if (AppContext.GetData("Example.Missing") is not null) return 4;
                    if (AppContext.TryGetSwitch("System.Resources.UseSystemResourceKeys", out _)) return 5;
                    AppContext.SetData("Example.Text", "set");
                    if (AppContext.GetData("Example.Text") as string != "set") return 6;
                    AppContext.SetSwitch("Example.Flag", false);
                    if (!AppContext.TryGetSwitch("Example.Flag", out flag) || flag) return 7;
                    return input + 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "RuntimePropertiesFixture.EntryPoint", "Run", []) with
        {
            RuntimeProperties = [("Example.Flag", "true"), ("Example.Off", "False"), ("Example.Text", "a=b c")],
        });

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // A package's ILLink.Substitutions.xml stubs a method under a feature switch's value, as Microsoft.Extensions
    // packages stub System.SR.UsingResourceKeys() for System.Resources.UseSystemResourceKeys: the project's folded
    // switch selects the stub, and the resource path leaves the program.
    [Theory]
    [InlineData(true, 42)]
    [InlineData(false, 124)]
    public void APackagesSubstitutionStubsTheMethodTheSwitchSelects(bool usingKeys, int expected)
    {
        using var assets = TestAssets.Create();
        var substitutions = Path.Combine(assets.Directory, "ILLink.Substitutions.xml");
        File.WriteAllText(substitutions, """
            <linker>
              <assembly fullname="SubstitutionFixture" feature="System.Resources.UseSystemResourceKeys" featurevalue="true">
                <type fullname="System.SR">
                  <method signature="System.Boolean UsingResourceKeys()" body="stub" value="true" />
                  <method signature="System.String Unsupported()" body="stub" value="x" />
                </type>
              </assembly>
            </linker>
            """);
        var assembly = assets.CompileSourceWithResources("SubstitutionFixture", """
            using System;
            using System.Runtime.CompilerServices;
            namespace System
            {
                internal static class SR
                {
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    internal static bool UsingResourceKeys() => AppContext.TryGetSwitch("Example.Keys", out var keys) && keys;
                }
            }
            namespace SubstitutionFixture
            {
                public static class Resources
                {
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static int Lookup(int input) => input * 3 + 1;
                }

                public static class EntryPoint
                {
                    public static int Run(int input) => System.SR.UsingResourceKeys() ? input + 1 : Resources.Lookup(input);
                }
            }
            """, [(substitutions, "ILLink.Substitutions.xml")]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "SubstitutionFixture.EntryPoint", "Run", []) with
        {
            FeatureSwitches = ImmutableDictionary<string, bool>.Empty.Add("System.Resources.UseSystemResourceKeys", usingKeys),
        });

        Assert.Equal(!usingKeys, Reaches(result, "Lookup"));
        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    private const string Switch = "FeatureSwitchFixture.Feature.IsSupported";

    private static bool Reaches(CompilationResult result, string name) =>
        result.Program.Methods.Values.Any(body => body.Method.Definition.Name == name);

    private static CompilerOptions Options(TestAssets assets, string assembly, WasmTarget target) =>
        new(assembly, [assets.CoreLib], "FeatureSwitchFixture.EntryPoint", "Run", [], Target: target);

    private const string Source = """
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;
        namespace FeatureSwitchFixture;

        public static class Feature
        {
            [FeatureSwitchDefinition("FeatureSwitchFixture.Feature.IsSupported")]
            public static bool IsSupported =>
                !AppContext.TryGetSwitch("FeatureSwitchFixture.Feature.IsSupported", out var enabled) || enabled;
        }

        public static class Guarded
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Expensive(int input) => input * 3 + 1;
        }

        public static class EntryPoint
        {
            public static int Run(int input) => Feature.IsSupported ? Guarded.Expensive(input) : input + 1;
        }
        """;
}
