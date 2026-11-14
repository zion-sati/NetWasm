using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// CoreLib embeds upstream's ILLink.Substitutions.xml, and the compiler applies its stubs as ILLink does for Mono: while
// EventSource is unsupported (the SDK's EventSourceSupport=false) every IsEnabled overload returns false, so the code
// each guards leaves the program, the pool's own logging and a user's EventSource alike, and Finalize does nothing.
public sealed class EventSourceStubCompilationTests
{
    private const string Switch = "System.Diagnostics.Tracing.EventSource.IsSupported";

    private const string Source = """
        using System;
        using System.Buffers;
        using System.Diagnostics.Tracing;

        namespace EventSourceFixture;

        [EventSource(Name = "Fixture")]
        public sealed class FixtureSource : EventSource
        {
            public static readonly FixtureSource Log = new();

            [Event(1)]
            public void Counted(int value) => WriteEvent(1, value);
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var array = ArrayPool<byte>.Shared.Rent(64);
                array[0] = 1;
                ArrayPool<byte>.Shared.Return(array);
                if (FixtureSource.Log.IsEnabled()) FixtureSource.Log.Counted(input);
                if (FixtureSource.Log.IsEnabled(EventLevel.Informational, EventKeywords.None)) FixtureSource.Log.Counted(input + 1);
                if (FixtureSource.Log.IsEnabled(EventLevel.Verbose, (EventKeywords)4, EventChannel.Debug)) FixtureSource.Log.Counted(input + 2);
                return input + array[0];
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void UnsupportedEventSourcesLeaveNothingInTheProgram(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("EventSourceOff", Source) : assets.CompileSource("EventSourceOff", Source);

        var result = NetWasmCompiler.Compile(Options(assets, assembly, target, false));

        Assert.DoesNotContain(ReachedTypes(result), IsEventSource);
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    // With the switch on the stubs do not apply: IsEnabled reads the source's state, and the logging stays.
    [Fact]
    public void SupportedEventSourcesKeepTheirLogging()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EventSourceOn", Source);

        var result = NetWasmCompiler.Compile(Options(assets, assembly, WasmTarget.Wasm32, true));

        Assert.Contains(ReachedTypes(result), type => type == "System.Buffers.ArrayPoolEventSource");
        Assert.Contains(ReachedTypes(result), type => type == "EventSourceFixture.FixtureSource");
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }

    // An event raised without asking IsEnabled first still runs, and does nothing.
    [Fact]
    public void AnUnguardedEventDoesNothing()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EventSourceUnguarded", Source.Replace(
            "return input + array[0];", "FixtureSource.Log.Counted(input); return input + array[0];", StringComparison.Ordinal));

        var result = NetWasmCompiler.Compile(Options(assets, assembly, WasmTarget.Wasm32, false));

        Assert.DoesNotContain(result.Program.Methods.Values, body => body.Method.Definition.Name.StartsWith("WriteEventCore",
            StringComparison.Ordinal));
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }


    private static CompilerOptions Options(TestAssets assets, string assembly, WasmTarget target, bool supported) =>
        new(assembly, [assets.CoreLib], "EventSourceFixture.EntryPoint", "Run", [], Target: target)
        {
            FeatureSwitches = ImmutableDictionary<string, bool>.Empty.Add(Switch, supported),
        };

    private static bool IsEventSource(string? type) =>
        type is "System.Diagnostics.Tracing.EventSource" or "System.Buffers.ArrayPoolEventSource" or "EventSourceFixture.FixtureSource";

    private static IEnumerable<string?> ReachedTypes(CompilationResult result) =>
        result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .Distinct();
}
