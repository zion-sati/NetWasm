using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Tests.Planning;

public sealed class RuntimeMaterializationCacheDescriptorBuilderTests
{
    [Fact]
    public void BuildsStableOpaqueIdentity()
    {
        var builder = new RuntimeMaterializationCacheDescriptorBuilder();
        var request = Request();

        var first = builder.Build(request);
        var second = builder.Build(request);

        Assert.Equal("runtime-materialization-cache-v1", first.Schema);
        Assert.Equal(first, second);
        Assert.All([first.Namespace, first.Slot, first.Key], value =>
        {
            Assert.Equal(64, value.Length);
            Assert.All(value, character => Assert.True(Uri.IsHexDigit(character)));
        });
    }

    [Fact]
    public void LayoutChangeReplacesIdentityWithinTheSameSlot()
    {
        var builder = new RuntimeMaterializationCacheDescriptorBuilder();
        var original = builder.Build(Request());
        var changed = builder.Build(Request() with
        {
            RuntimeGlobalBase = 131_072,
            HeapBase = 196_608,
            Arguments = ["--global-base=131072", "output.wasm"],
        });

        Assert.Equal(original.Namespace, changed.Namespace);
        Assert.Equal(original.Slot, changed.Slot);
        Assert.NotEqual(original.Key, changed.Key);
    }

    [Theory]
    [InlineData("wasm64", RuntimeWasmOptimization.Oz)]
    [InlineData("wasm32", RuntimeWasmOptimization.None)]
    [InlineData("wasm32", RuntimeWasmOptimization.O3)]
    public void TargetOrModeSelectsAnotherSlot(
        string target,
        RuntimeWasmOptimization optimization)
    {
        var builder = new RuntimeMaterializationCacheDescriptorBuilder();
        var original = builder.Build(Request());
        var changed = builder.Build(Request() with
        {
            Target = target,
            Optimization = optimization,
            OptimizationArguments = optimization == RuntimeWasmOptimization.None
                ? []
                : [$"-{optimization}", "output.wasm"],
        });

        Assert.Equal(original.Namespace, changed.Namespace);
        Assert.NotEqual(original.Slot, changed.Slot);
        Assert.NotEqual(original.Key, changed.Key);
    }

    [Fact]
    public void OrderedPlanInputsParticipateInTheIdentity()
    {
        var builder = new RuntimeMaterializationCacheDescriptorBuilder();
        var original = builder.Build(Request());
        var arguments = builder.Build(Request() with
        {
            Arguments = ["output.wasm", "--global-base=65536"],
        });
        var optimization = builder.Build(Request() with
        {
            OptimizationArguments = ["output.wasm", "-Oz"],
        });
        var inputs = builder.Build(Request() with
        {
            Inputs = Request().Inputs.Reverse().ToImmutableArray(),
        });

        Assert.Equal(4, new[] { original.Key, arguments.Key, optimization.Key, inputs.Key }.Distinct().Count());
    }

    [Fact]
    public void RuntimeAndToolchainPolicyParticipateInTheIdentity()
    {
        var builder = new RuntimeMaterializationCacheDescriptorBuilder();
        var original = builder.Build(Request());
        var runtime = builder.Build(Request() with { RuntimeAbi = "runtime-abi-2" });
        var toolchain = builder.Build(Request() with { ToolchainFingerprint = "toolchain-2" });
        var memory = builder.Build(Request() with { MaximumMemorySizeBytes = 1_073_741_824 });

        Assert.Equal(4, new[] { original.Key, runtime.Key, toolchain.Key, memory.Key }.Distinct().Count());
        Assert.Equal(original.Slot, runtime.Slot);
        Assert.Equal(original.Slot, toolchain.Slot);
        Assert.Equal(original.Slot, memory.Slot);
    }

    [Fact]
    public void RejectsMissingRequest()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeMaterializationCacheDescriptorBuilder().Build(null!));
    }

    [Theory]
    [InlineData(InvalidRequest.Target)]
    [InlineData(InvalidRequest.RuntimeAbi)]
    [InlineData(InvalidRequest.ToolchainFingerprint)]
    [InlineData(InvalidRequest.Optimization)]
    [InlineData(InvalidRequest.Arguments)]
    [InlineData(InvalidRequest.OptimizationArguments)]
    [InlineData(InvalidRequest.Inputs)]
    public void RejectsInvalidRequest(InvalidRequest invalid)
    {
        var request = invalid switch
        {
            InvalidRequest.Target => Request() with { Target = "" },
            InvalidRequest.RuntimeAbi => Request() with { RuntimeAbi = "" },
            InvalidRequest.ToolchainFingerprint => Request() with { ToolchainFingerprint = "" },
            InvalidRequest.Optimization => Request() with { Optimization = (RuntimeWasmOptimization)42 },
            InvalidRequest.Arguments => Request() with { Arguments = default },
            InvalidRequest.OptimizationArguments => Request() with { OptimizationArguments = default },
            InvalidRequest.Inputs => Request() with { Inputs = default },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };

        Assert.ThrowsAny<ArgumentException>(() =>
            new RuntimeMaterializationCacheDescriptorBuilder().Build(request));
    }

    public enum InvalidRequest
    {
        Target,
        RuntimeAbi,
        ToolchainFingerprint,
        Optimization,
        Arguments,
        OptimizationArguments,
        Inputs,
    }

    private static RuntimeMaterializationCacheDescriptorRequest Request() => new(
        "wasm32",
        RuntimeWasmOptimization.Oz,
        "runtime-abi",
        "toolchain",
        65_536,
        131_072,
        196_608,
        2_147_483_648,
        ["--global-base=65536", "output.wasm"],
        ["-Oz", "output.wasm"],
        [
            new("/runtime/runtime.a", new string('a', 64)),
            new("/runtime/libc.a", new string('b', 64)),
        ]);
}
