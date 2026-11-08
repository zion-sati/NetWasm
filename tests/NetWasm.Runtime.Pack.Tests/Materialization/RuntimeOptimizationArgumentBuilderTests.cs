using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeOptimizationArgumentBuilderTests
{
    [Fact]
    public void RejectsMissingPolicy() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimeOptimizationArgumentBuilder(null!));

    [Fact]
    public void UsesResolvedFlagRatherThanItsOwnPresetTable()
    {
        var policy = new FixedPolicy();
        var builder = Assert.IsAssignableFrom<IRuntimeOptimizationArgumentBuilder>(new RuntimeOptimizationArgumentBuilder(policy));
        var arguments = builder.Build(new(RuntimePackTestData.Target("wasm32"), "/runtime.wasm", RuntimeWasmOptimization.Oz));

        Assert.Equal(RuntimeWasmOptimization.Oz, policy.Mode);
        Assert.Equal("-O2", Assert.Single(arguments, IsOptimizationFlag));
    }

    private sealed class FixedPolicy : IRuntimeOptimizationPolicyResolver
    {
        public RuntimeWasmOptimization? Mode { get; private set; }

        public RuntimeOptimizationPolicy Resolve(RuntimeWasmOptimization optimization)
        {
            Mode = optimization;
            return new("-O2", false);
        }
    }

    [Theory]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O0, "-O0")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O1, "-O1")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O2, "-O2")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O3, "-O3")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.Os, "-Os")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.Oz, "-Oz")]
    [InlineData("wasm64", true, RuntimeWasmOptimization.Oz, "-Oz")]
    public void ReproducesPinnedEmscriptenOptimizationWithSelectedMode(
        string target,
        bool memory64,
        RuntimeWasmOptimization optimization,
        string optimizationFlag)
    {
        using var directory = new TemporaryDirectory();
        var output = directory.PathTo("runtime.wasm");

        var arguments = new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
            RuntimePackTestData.Target(target), output, optimization)
        {
            RuntimeGlobalBase = 1024,
        });

        Assert.Contains("--post-emscripten", arguments);
        Assert.Equal(optimizationFlag, Assert.Single(arguments, IsOptimizationFlag));
        Assert.Contains("--low-memory-unused", arguments);
        Assert.Contains("--strip-debug", arguments);
        Assert.Contains("--strip-producers", arguments);
        Assert.Equal(2, arguments.Count(argument => argument == Path.GetFullPath(output)));
        Assert.Equal(memory64, arguments.Contains("--enable-memory64"));
    }

    [Fact]
    public void KeepsLowMemoryLiveWhenStaticDataStartsBelowTheBinaryenBoundary()
    {
        using var directory = new TemporaryDirectory();

        var arguments = new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
            RuntimePackTestData.Target("wasm32"),
            directory.PathTo("runtime.wasm"),
            RuntimeWasmOptimization.Oz)
        {
            RuntimeGlobalBase = 1008,
        });

        Assert.DoesNotContain("--low-memory-unused", arguments);
    }

    [Fact]
    public void NoneProducesNoOptimizerArguments()
    {
        using var directory = new TemporaryDirectory();
        Assert.Empty(new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
            RuntimePackTestData.Target("wasm32"), directory.PathTo("runtime.wasm"),
            RuntimeWasmOptimization.None)));
    }

    [Fact]
    public void RejectsUndefinedOptimization() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
                RuntimePackTestData.Target("wasm32"), "runtime.wasm",
                (RuntimeWasmOptimization)42)));

    // Binaryen enables what the pack's static-native profile allows, under its own names:
    // SIMD archives optimize, and wasm64 adds memory64.
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void EnablesTheStaticNativeProfilesFeatures(string target)
    {
        var arguments = new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
            RuntimePackTestData.Target(target), "/runtime.wasm", RuntimeWasmOptimization.Oz));

        List<string> expected =
        [
            "--mvp-features", "--enable-bulk-memory", "--enable-bulk-memory-opt", "--enable-call-indirect-overlong",
            "--enable-multivalue", "--enable-mutable-globals", "--enable-nontrapping-float-to-int",
            "--enable-reference-types", "--enable-sign-ext", "--enable-simd",
        ];
        if (target == "wasm64") expected.Insert(4, "--enable-memory64");
        Assert.Equal(expected, arguments.SkipWhile(argument => argument != "--mvp-features"));
    }

    public static TheoryData<string, string[]?> UnusableProfiles => new()
    {
        { "no profile", null },
        { "no features", [] },
        { "mvp not first", ["simd", "mvp"] },
        { "an unknown feature", ["mvp", "tail-call"] },
    };

    [Theory]
    [MemberData(nameof(UnusableProfiles))]
    public void RejectsAProfileItCannotTranslate(string profile, string[]? features)
    {
        _ = profile;
        var target = RuntimePackTestData.Target("wasm32") with
        {
            NativeValidation = features is null ? null : RuntimePackTestData.NativeProfile() with { Features = [.. features] },
        };

        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(new(
                target, "/runtime.wasm", RuntimeWasmOptimization.Oz)));
    }

    [Fact]
    public void RejectsMissingRequest() =>
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeOptimizationArgumentBuilder(new RuntimeOptimizationPolicyResolver()).Build(null!));

    private static bool IsOptimizationFlag(string argument) =>
        argument is "-O0" or "-O1" or "-O2" or "-O3" or "-Os" or "-Oz";
}
