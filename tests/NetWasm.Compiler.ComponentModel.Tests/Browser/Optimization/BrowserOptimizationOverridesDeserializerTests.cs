using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Browser.Optimization;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Optimization;

public sealed class BrowserOptimizationOverridesDeserializerTests
{
    [Fact]
    public void PreservesEveryCompilerOwnedOverrideWithoutResolvingPolicy()
    {
        var result = Create().Deserialize("""
            {
              "managedInliningHints":"IgnoreAggressive",
              "loopInvariantFieldHoisting":"Enabled",
              "staticDataViewElimination":"Disabled",
              "addressTakenValuePromotion":"Enabled",
              "managedMethodInlining":"Disabled",
              "branchPrefixDuplication":"Enabled",
              "scalarCallPreservation":"Disabled",
              "arrayBoundsCheckElimination":"Enabled",
              "loopUnrolling":"Disabled",
              "constantArgumentPropagation":"Enabled",
              "staticArrayPopulationElimination":"Disabled",
              "constantCallFolding":"Enabled",
              "switchBitTests":"Disabled",
              "devirtualization":"Enabled",
              "allocationElimination":"Disabled",
              "loopRotation":"Enabled",
              "arrayStoreCheckElimination":"Disabled",
              "dispatcherElimination":"Enabled",
              "emptyFinallyRemoval":"Disabled",
              "structScalarReplacement":"Enabled",
              "allocationScalarReplacement":"Disabled",
              "idempotentLoopCollapse":"Enabled",
              "loopStoreForwarding":"Disabled",
              "arrayLoopIdioms":"Enabled",
              "loopVectorization":"Disabled"
            }
            """);
        Assert.Equal(new WasmOptimizationOverrides(
            ManagedInliningHints.IgnoreAggressive,
            ManagedLoopInvariantFieldHoisting.Enabled,
            ManagedStaticDataViewElimination.Disabled,
            ManagedAddressTakenValuePromotion.Enabled,
            ManagedMethodInlining.Disabled,
            ManagedBranchPrefixDuplication.Enabled,
            ManagedScalarCallPreservation.Disabled,
            ManagedArrayBoundsCheckElimination.Enabled,
            ManagedLoopUnrolling.Disabled,
            ManagedConstantArgumentPropagation.Enabled,
            ManagedStaticArrayPopulationElimination.Disabled,
            ManagedConstantCallFolding.Enabled,
            ManagedSwitchBitTests.Disabled,
            ManagedDevirtualization.Enabled,
            ManagedAllocationElimination.Disabled,
            ManagedLoopRotation.Enabled,
            ManagedArrayStoreCheckElimination.Disabled,
            ManagedDispatcherElimination.Enabled,
            ManagedEmptyFinallyRemoval.Disabled,
            ManagedStructScalarReplacement.Enabled,
            ManagedAllocationScalarReplacement.Disabled,
            ManagedIdempotentLoopCollapse.Enabled,
            ManagedLoopStoreForwarding.Disabled,
            ManagedArrayLoopIdioms.Enabled,
            ManagedLoopVectorization.Disabled), result);
    }

    [Fact]
    public void MissingOverridesAndMembersRetainCompilerDefaults()
    {
        var deserializer = Create();
        Assert.Null(deserializer.Deserialize("null"));
        Assert.Equal(new WasmOptimizationOverrides(), deserializer.Deserialize("{}"));
        Assert.Equal(new WasmOptimizationOverrides(ManagedInliningHints.Preserve),
            deserializer.Deserialize("{\"managedInliningHints\":\"Preserve\"}"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"unknown\":\"Enabled\"}")]
    [InlineData("{\"loopUnrolling\":\"Unknown\"}")]
    public void InvalidContractsFailExplicitly(string json) =>
        Assert.Throws<JsonException>(() => Create().Deserialize(json));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingJsonIsRejected(string? json) =>
        Assert.ThrowsAny<ArgumentException>(() => Create().Deserialize(json!));

    private static IBrowserOptimizationOverridesDeserializer Create() =>
        Assert.IsAssignableFrom<IBrowserOptimizationOverridesDeserializer>(new BrowserOptimizationOverridesDeserializer());
}
