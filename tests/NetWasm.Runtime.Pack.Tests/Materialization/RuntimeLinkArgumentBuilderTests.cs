using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeLinkArgumentBuilderTests
{
    [Fact]
    public void RejectsMissingExportPlanningCapability() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimeLinkArgumentBuilder(null!));

    [Theory]
    [InlineData("wasm32", "-mwasm32")]
    [InlineData("wasm64", "-mwasm64")]
    public void BuildsTargetSpecificClosedWorldLinkArguments(string target, string machine)
    {
        using var directory = new TemporaryDirectory();
        var manifest = RuntimePackTestData.Manifest();
        var selected = RuntimePackTestData.Target(target);
        var systemLibrary = directory.PathTo(Path.Combine(target, "libc.a"));
        var arguments = new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()).Build(new(
            manifest,
            selected,
            RuntimePackTestData.LinkLimits(target),
            directory.Path,
            [systemLibrary],
            directory.PathTo("output/runtime.wasm")));

        Assert.Equal(machine, arguments[0]);
        Assert.Contains("--whole-archive", arguments);
        Assert.Contains(Path.GetFullPath(directory.PathTo(selected.RuntimeArchive.Path)), arguments);
        Assert.Contains(Path.GetFullPath(directory.PathTo(selected.CollectorArchive.Path)), arguments);
        Assert.Contains(Path.GetFullPath(systemLibrary), arguments);
        Assert.DoesNotContain("--undefined=__emscripten_environ_constructor", arguments);
        Assert.Contains("--no-stack-first", arguments);
        Assert.Contains("--strip-debug", arguments);
        Assert.Contains("--export=emscripten_stack_get_current", arguments);
        Assert.Contains("--export=_emscripten_stack_restore", arguments);
        Assert.Contains("-combiner-global-alias-analysis=false", arguments);
        Assert.Contains($"--allow-undefined-file={Path.GetFullPath(
            directory.PathTo(selected.AllowedUndefinedSymbols.Path))}", arguments);
        Assert.Contains($"--global-base={RuntimePackTestData.Layout(target).RuntimeGlobalBase}", arguments);
        Assert.Contains("stack-size=65536", arguments);
        Assert.Contains($"--initial-memory={RuntimePackTestData.Layout(target).InitialMemorySizeBytes}", arguments);
        Assert.Contains($"--max-memory={RuntimePackTestData.Layout(target).MaximumMemorySizeBytes}", arguments);
        Assert.Contains("--export=initialize", arguments);
        Assert.Contains("--export=allocate", arguments);
        Assert.Equal(Path.GetFullPath(directory.PathTo("output/runtime.wasm")), arguments[^1]);
        Assert.DoesNotContain("--allow-multiple-definition", arguments);
    }

    [Fact]
    public void RejectsAssetOutsidePackRoot()
    {
        using var directory = new TemporaryDirectory();
        var target = RuntimePackTestData.Target("wasm32") with
        {
            RuntimeArchive = RuntimePackTestData.Asset("../outside.a"),
        };

        Assert.Throws<InvalidOperationException>(() => new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()).Build(new(
            RuntimePackTestData.Manifest(),
            target,
            RuntimePackTestData.LinkLimits(),
            directory.Path,
            [directory.PathTo("libc.a")],
            directory.PathTo("runtime.wasm"))));
    }

    [Theory]
    [InlineData("wasm32", "Boehm")]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm64", "Boehm")]
    [InlineData("wasm64", "Compact")]
    public void AcceptsAssetRootWithTrailingSeparator(string target, string collector)
    {
        using var directory = new TemporaryDirectory();
        var arguments = new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()).Build(new(
            RuntimePackTestData.Manifest(),
            RuntimePackTestData.Target(target, collector),
            RuntimePackTestData.LinkLimits(),
            directory.Path + Path.DirectorySeparatorChar,
            [directory.PathTo("libc.a")],
            directory.PathTo("runtime.wasm")));

        Assert.Contains(
            Path.GetFullPath(directory.PathTo($"{target}/{collector.ToLowerInvariant()}/libnetwasm-runtime.a")),
            arguments);
        Assert.Contains(
            Path.GetFullPath(directory.PathTo($"{target}/{collector.ToLowerInvariant()}/libgc.a")),
            arguments);
    }

    [Fact]
    public void RejectsNullRequest() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()).Build(null!));

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void NativeProbeUsesSelectedRootsOutsideWholeArchiveAndNoPredictedInitialMemory(string target)
    {
        var provider = new RuntimeNativeLibrary("mule", target, Path.GetFullPath("native/mule.a"), RuntimePackTestData.Digest);
        var request = NativeRequest(target, provider);
        var capability = Assert.IsAssignableFrom<IRuntimeLinkArgumentBuilder>(new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder()));
        var arguments = capability.Build(request);
        var nativeIndex = arguments.IndexOf(provider.Path);
        Assert.True(nativeIndex > arguments.IndexOf("--no-whole-archive"));
        Assert.Equal(1, arguments.Count(argument => argument == provider.Path));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--initial-memory=", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--initial-heap=", StringComparison.Ordinal));
        Assert.Contains("--global-base=65552", arguments);
        Assert.Contains("--max-memory=1048576", arguments);
        foreach (var name in new[] { "__global_base", "__data_end", "__stack_low", "__stack_high", "__heap_base" })
            Assert.Contains($"--export={name}", arguments);
        foreach (var symbol in new[] { "first", "second" })
        {
            Assert.Contains($"--undefined={symbol}", arguments);
            Assert.Contains($"--export={symbol}", arguments);
            Assert.Contains($"--trace-symbol={symbol}", arguments);
        }
        Assert.DoesNotContain("--whole-archive", arguments.Skip(nativeIndex));

        var final = capability.Build(request with { Layout = request.Layout with { InitialMemorySizeBytes = 262_144 } });
        Assert.Contains("--initial-memory=262144", final);
        Assert.Equal(arguments.Length + 1, final.Length);
        Assert.Equal(arguments.ToArray(), final.Where(argument => !argument.StartsWith("--initial-memory=", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void RejectsProbeWithoutNativeBindingsAndMissingRequiredInputs()
    {
        var request = NativeRequest("wasm32", new("mule", "wasm32", Path.GetFullPath("mule.a"), RuntimePackTestData.Digest));
        var builder = new RuntimeLinkArgumentBuilder(new RuntimeLinkExportPlanBuilder());
        Assert.Throws<InvalidOperationException>(() => builder.Build(request with { NativeBindings = [] }));
        Assert.Throws<InvalidOperationException>(() => builder.Build(request with { NativeBindings = default }));
        Assert.Throws<ArgumentNullException>(() => builder.Build(request with { Manifest = null! }));
        Assert.Throws<ArgumentNullException>(() => builder.Build(request with { Target = null! }));
        Assert.Throws<ArgumentNullException>(() => builder.Build(request with { Layout = null! }));
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void CallbackOnlyProbeLinksTheCompilerObjectWithExactRoots(string target)
    {
        using var directory = new TemporaryDirectory();
        var callbackObject = directory.PathTo("application.callbacks.o");
        var allowed = directory.PathTo("callbacks.allow-undefined");
        var support = RuntimePackTestData.CallbackSupport(target);
        var request = new RuntimeLinkRequest(
            RuntimePackTestData.Manifest(),
            RuntimePackTestData.Target(target),
            new(65_552, null, target == "wasm64" ? 8_589_934_592 : 2_147_483_648),
            directory.Path,
            [],
            directory.PathTo("runtime.wasm"))
        {
            NativeCallbackObjectPath = callbackObject,
            NativeCallbackAllowedUndefinedPath = allowed,
            NativeCallbackSupport = support,
        };

        var arguments = new RuntimeLinkArgumentBuilder(
            new RuntimeLinkExportPlanBuilder()).Build(request);

        Assert.Empty(request.NativeBindings);
        Assert.Contains(Path.GetFullPath(callbackObject), arguments);
        Assert.Contains($"--allow-undefined-file={Path.GetFullPath(allowed)}", arguments);
        Assert.Single(arguments.Where(argument =>
            argument.StartsWith("--allow-undefined-file=", StringComparison.Ordinal)));
        Assert.Contains("--export=__netwasm_callback_address_0", arguments);
        Assert.DoesNotContain(arguments, argument =>
            argument.StartsWith("--undefined=__netwasm_native_callback_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument =>
            argument.StartsWith("--trace-symbol=__netwasm_native_callback_",
                StringComparison.Ordinal));
        foreach (var name in new[]
        {
            "__global_base", "__data_end", "__stack_low", "__stack_high", "__heap_base",
        })
        {
            Assert.Contains($"--export={name}", arguments);
        }
    }

    private static RuntimeLinkRequest NativeRequest(string target, RuntimeNativeLibrary provider) => new(
        RuntimePackTestData.Manifest(), RuntimePackTestData.Target(target), new(65_552, null, 1_048_576),
        Path.GetFullPath("runtime-assets"), [], Path.GetFullPath("runtime.wasm"))
    {
        NativeBindings = [
            new(new("mule", "first", [], RuntimeNativeValueType.I32), provider),
            new(new("mule", "second", [], null), provider)],
    };
}
