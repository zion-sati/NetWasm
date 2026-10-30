using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Optimization;

public sealed class ManagedCompilerOptimizationOverridesParserTests
{
    public static TheoryData<int, string?> AdditionalSwitches
    {
        get
        {
            var data = new TheoryData<int, string?>();
            for (var feature = 0; feature < 18; feature++)
                foreach (var value in new string?[] { null, "Auto", "Disabled", "Enabled", "enabled", "", "2" })
                    data.Add(feature, value);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AdditionalSwitches))]
    public void ParsesEachAdditionalSwitchWithoutAffectingOtherOverrides(int feature, string? value)
    {
        var parser = new ManagedCompilerOptimizationOverridesParser();
        WasmOptimizationOverrides? Parse() => parser.Parse(null, null, null,
            arrayBoundsCheckElimination: feature == 0 ? value : null,
            loopUnrolling: feature == 1 ? value : null,
            constantArgumentPropagation: feature == 2 ? value : null,
            staticArrayPopulationElimination: feature == 3 ? value : null,
            constantCallFolding: feature == 4 ? value : null,
            switchBitTests: feature == 5 ? value : null,
            devirtualization: feature == 6 ? value : null,
            allocationElimination: feature == 7 ? value : null,
            loopRotation: feature == 8 ? value : null,
            arrayStoreCheckElimination: feature == 9 ? value : null,
            dispatcherElimination: feature == 10 ? value : null,
            emptyFinallyRemoval: feature == 11 ? value : null,
            structScalarReplacement: feature == 12 ? value : null,
            allocationScalarReplacement: feature == 13 ? value : null,
            idempotentLoopCollapse: feature == 14 ? value : null,
            loopStoreForwarding: feature == 15 ? value : null,
            arrayLoopIdioms: feature == 16 ? value : null,
            loopVectorization: feature == 17 ? value : null);
        if (value is null or "Auto")
        {
            Assert.Null(Parse());
            return;
        }
        if (value is not ("Disabled" or "Enabled"))
        {
            Assert.Throws<CompilerException>(() => Parse());
            return;
        }
        var enabled = value == "Enabled";
        var expected = feature switch
        {
            0 => new WasmOptimizationOverrides(ArrayBoundsCheckElimination: enabled
                ? ManagedArrayBoundsCheckElimination.Enabled : ManagedArrayBoundsCheckElimination.Disabled),
            1 => new WasmOptimizationOverrides(LoopUnrolling: enabled
                ? ManagedLoopUnrolling.Enabled : ManagedLoopUnrolling.Disabled),
            2 => new WasmOptimizationOverrides(ConstantArgumentPropagation: enabled
                ? ManagedConstantArgumentPropagation.Enabled : ManagedConstantArgumentPropagation.Disabled),
            3 => new WasmOptimizationOverrides(StaticArrayPopulationElimination: enabled
                ? ManagedStaticArrayPopulationElimination.Enabled : ManagedStaticArrayPopulationElimination.Disabled),
            4 => new WasmOptimizationOverrides(ConstantCallFolding: enabled
                ? ManagedConstantCallFolding.Enabled : ManagedConstantCallFolding.Disabled),
            5 => new WasmOptimizationOverrides(SwitchBitTests: enabled
                ? ManagedSwitchBitTests.Enabled : ManagedSwitchBitTests.Disabled),
            6 => new WasmOptimizationOverrides(Devirtualization: enabled
                ? ManagedDevirtualization.Enabled : ManagedDevirtualization.Disabled),
            7 => new WasmOptimizationOverrides(AllocationElimination: enabled
                ? ManagedAllocationElimination.Enabled : ManagedAllocationElimination.Disabled),
            8 => new WasmOptimizationOverrides(LoopRotation: enabled
                ? ManagedLoopRotation.Enabled : ManagedLoopRotation.Disabled),
            9 => new WasmOptimizationOverrides(ArrayStoreCheckElimination: enabled
                ? ManagedArrayStoreCheckElimination.Enabled : ManagedArrayStoreCheckElimination.Disabled),
            10 => new WasmOptimizationOverrides(DispatcherElimination: enabled
                ? ManagedDispatcherElimination.Enabled : ManagedDispatcherElimination.Disabled),
            11 => new WasmOptimizationOverrides(EmptyFinallyRemoval: enabled
                ? ManagedEmptyFinallyRemoval.Enabled : ManagedEmptyFinallyRemoval.Disabled),
            12 => new WasmOptimizationOverrides(StructScalarReplacement: enabled
                ? ManagedStructScalarReplacement.Enabled : ManagedStructScalarReplacement.Disabled),
            13 => new WasmOptimizationOverrides(AllocationScalarReplacement: enabled
                ? ManagedAllocationScalarReplacement.Enabled : ManagedAllocationScalarReplacement.Disabled),
            14 => new WasmOptimizationOverrides(IdempotentLoopCollapse: enabled
                ? ManagedIdempotentLoopCollapse.Enabled : ManagedIdempotentLoopCollapse.Disabled),
            15 => new WasmOptimizationOverrides(LoopStoreForwarding: enabled
                ? ManagedLoopStoreForwarding.Enabled : ManagedLoopStoreForwarding.Disabled),
            16 => new WasmOptimizationOverrides(ArrayLoopIdioms: enabled
                ? ManagedArrayLoopIdioms.Enabled : ManagedArrayLoopIdioms.Disabled),
            _ => new WasmOptimizationOverrides(LoopVectorization: enabled
                ? ManagedLoopVectorization.Enabled : ManagedLoopVectorization.Disabled),
        };
        Assert.Equal(expected, Parse());
    }

    [Theory]
    [InlineData(null, ManagedScalarCallPreservation.Auto)]
    [InlineData("Auto", ManagedScalarCallPreservation.Auto)]
    [InlineData("Disabled", ManagedScalarCallPreservation.Disabled)]
    [InlineData("Enabled", ManagedScalarCallPreservation.Enabled)]
    public void ParsesScalarCallPreservation(string? value,
        ManagedScalarCallPreservation expected)
    {
        var actual = new ManagedCompilerOptimizationOverridesParser().Parse(
            null, null, null, scalarCallPreservation: value);
        if (expected == ManagedScalarCallPreservation.Auto)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.Equal(new(ScalarCallPreservation: expected), actual);
        }
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("")]
    [InlineData("Preserve")]
    public void RejectsNoncanonicalScalarCallPreservation(string value)
    {
        var error = Assert.Throws<CompilerException>(() =>
            new ManagedCompilerOptimizationOverridesParser().Parse(
                null, null, null, scalarCallPreservation: value));
        Assert.Contains("scalar-call preservation", error.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultsToNoOverrides()
    {
        var parser = Assert.IsAssignableFrom<IManagedCompilerOptimizationOverridesParser>(
            new ManagedCompilerOptimizationOverridesParser());

        Assert.Null(parser.Parse(null, "Auto", null));
        Assert.Null(parser.Parse("Auto", null, "Auto"));
    }

    [Theory]
    [InlineData("Auto", "Enabled", "Auto", ManagedLoopInvariantFieldHoisting.Auto,
        ManagedStaticDataViewElimination.Enabled, ManagedAddressTakenValuePromotion.Auto)]
    [InlineData("Disabled", "Auto", "Enabled", ManagedLoopInvariantFieldHoisting.Disabled,
        ManagedStaticDataViewElimination.Auto, ManagedAddressTakenValuePromotion.Enabled)]
    [InlineData("Enabled", "Disabled", "Disabled", ManagedLoopInvariantFieldHoisting.Enabled,
        ManagedStaticDataViewElimination.Disabled, ManagedAddressTakenValuePromotion.Disabled)]
    [InlineData("Auto", "Auto", "References", ManagedLoopInvariantFieldHoisting.Auto,
        ManagedStaticDataViewElimination.Auto, ManagedAddressTakenValuePromotion.References)]
    [InlineData("NullChecks", "Auto", "Auto", ManagedLoopInvariantFieldHoisting.NullChecks,
        ManagedStaticDataViewElimination.Auto, ManagedAddressTakenValuePromotion.Auto)]
    public void ParsesCanonicalCompilerOwnedSelections(
        string fieldHoisting,
        string staticDataViews,
        string valuePromotion,
        ManagedLoopInvariantFieldHoisting expectedFieldHoisting,
        ManagedStaticDataViewElimination expectedStaticDataViews,
        ManagedAddressTakenValuePromotion expectedValuePromotion)
    {
        var actual = new ManagedCompilerOptimizationOverridesParser().Parse(
            fieldHoisting,
            staticDataViews,
            valuePromotion);

        Assert.NotNull(actual);
        Assert.Equal(expectedFieldHoisting, actual.LoopInvariantFieldHoisting);
        Assert.Equal(expectedStaticDataViews, actual.StaticDataViewElimination);
        Assert.Equal(expectedValuePromotion, actual.AddressTakenValuePromotion);
        Assert.Equal(ManagedInliningHints.Auto, actual.ManagedInliningHints);
        Assert.Equal(ManagedMethodInlining.Auto, actual.ManagedMethodInlining);
    }

    [Theory]
    [InlineData("Auto", ManagedMethodInlining.Auto)]
    [InlineData("Disabled", ManagedMethodInlining.Disabled)]
    [InlineData("Enabled", ManagedMethodInlining.Enabled)]
    [InlineData("Loops", ManagedMethodInlining.Loops)]
    public void ParsesManagedMethodInlining(
        string value,
        ManagedMethodInlining expected)
    {
        var actual = new ManagedCompilerOptimizationOverridesParser().Parse(
            "Auto",
            "Auto",
            "Auto",
            value);

        if (expected == ManagedMethodInlining.Auto)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.NotNull(actual);
            Assert.Equal(expected, actual.ManagedMethodInlining);
        }
    }

    [Theory]
    [InlineData("enabled", "Auto", "Auto", "field hoisting")]
    [InlineData("Auto", "enabled", "Auto", "static-data-view")]
    [InlineData("Preserve", "Auto", "Auto", "field hoisting")]
    [InlineData("Auto", "", "Auto", "static-data-view")]
    [InlineData("Auto", "Auto", "enabled", "address-taken value")]
    public void RejectsUnknownOrNoncanonicalSelections(
        string fieldHoisting,
        string staticDataViews,
        string valuePromotion,
        string expectedMessage)
    {
        var error = Assert.Throws<CompilerException>(() =>
            new ManagedCompilerOptimizationOverridesParser().Parse(
                fieldHoisting,
                staticDataViews,
                valuePromotion));

        Assert.Contains(expectedMessage, error.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Auto, Disabled or Enabled", error.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownManagedMethodInliningSelection()
    {
        var error = Assert.Throws<CompilerException>(() =>
            new ManagedCompilerOptimizationOverridesParser().Parse(
                "Auto",
                "Auto",
                "Auto",
                "enabled"));

        Assert.Contains("method inlining", error.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Auto, Disabled, Enabled or Loops", error.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Auto", ManagedBranchPrefixDuplication.Auto)]
    [InlineData("Disabled", ManagedBranchPrefixDuplication.Disabled)]
    [InlineData("Enabled", ManagedBranchPrefixDuplication.Enabled)]
    public void ParsesBranchPrefixDuplication(
        string value,
        ManagedBranchPrefixDuplication expected)
    {
        var actual = new ManagedCompilerOptimizationOverridesParser().Parse(
            "Auto",
            "Auto",
            "Auto",
            "Auto",
            value);

        if (expected == ManagedBranchPrefixDuplication.Auto)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.NotNull(actual);
            Assert.Equal(expected, actual.BranchPrefixDuplication);
        }
    }

    [Fact]
    public void RejectsUnknownBranchPrefixDuplicationSelection()
    {
        var error = Assert.Throws<CompilerException>(() =>
            new ManagedCompilerOptimizationOverridesParser().Parse(
                "Auto",
                "Auto",
                "Auto",
                "Auto",
                "enabled"));

        Assert.Contains("branch-prefix duplication", error.Diagnostic.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Auto, Disabled or Enabled", error.Diagnostic.Message,
            StringComparison.Ordinal);
    }
}
