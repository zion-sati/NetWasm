using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserManagedInliningTests
{
    [Theory]
    [MemberData(nameof(ModesAndWidths))]
    public void ExplicitHintsHaveIdenticalRawAndComponentSemantics(
        FinalWasmOptimization optimization, ComponentTarget target)
    {
        foreach (var hint in new[] { ManagedInliningHints.IgnoreAggressive, ManagedInliningHints.Preserve })
        {
            var overrides = new WasmOptimizationOverrides(hint);
            var request = new ComponentCoreModuleLinkRequest("application", "runtime", "output", target,
                Optimization: optimization)
            { OptimizationOverrides = overrides };
            var rawRequest = new RawModuleLinkRequest("application", "runtime", "output", target, optimization)
            { OptimizationOverrides = overrides };
            if (optimization == FinalWasmOptimization.None)
            {
                Assert.Throws<ArgumentException>(() => BrowserComponentCoreModules.CreateLinkPlan(request,
                    new("environment", "host", "adapter", "merged", "sanitized", "runtime-sanitized")));
                Assert.Throws<ArgumentException>(() => BrowserRawCoreModules.CreateLinkPlan(rawRequest,
                    new("temporary", "linked")));
                continue;
            }
            var component = BrowserComponentCoreModules.CreateLinkPlan(request,
                new("environment", "host", "adapter", "merged", "sanitized", "runtime-sanitized"));
            var raw = BrowserRawCoreModules.CreateLinkPlan(rawRequest, new("temporary", "linked"));
            var expected = hint == ManagedInliningHints.Preserve
                ? WasmInlineAnnotationSelection.Preserve : WasmInlineAnnotationSelection.SizeAware;
            Assert.Equal(expected, component.Operations.OfType<BrowserLinkInlineOperation>().First().Selection);
            Assert.Equal(expected, raw.Operations.OfType<BrowserLinkInlineOperation>().First().Selection);
            var runtimeExpected = IsSpeedProfile(optimization)
                ? WasmInlineAnnotationSelection.Preserve
                : WasmInlineAnnotationSelection.SizeAware;
            Assert.Equal(runtimeExpected, component.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First().Selection);
            Assert.Equal(runtimeExpected, raw.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First().Selection);
        }
    }

    [Fact]
    public void UnknownModeKeepsItsOriginalDiagnosticDespiteWorkspaceCleanup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            new("application", "runtime", "output", ComponentTarget.Wasm32Wasi02,
                Optimization: (FinalWasmOptimization)255),
            new("environment", "host", "adapter", "merged", "sanitized", "runtime-sanitized")));
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserRawCoreModules.CreateLinkPlan(
            new("application", "runtime", "output", ComponentTarget.Wasm32Wasi02,
                (FinalWasmOptimization)255), new("temporary", "linked")));
    }

    public static IEnumerable<object[]> ModesAndWidths =>
        from mode in Enum.GetValues<FinalWasmOptimization>()
        from target in new[] { ComponentTarget.Wasm32Wasi02, ComponentTarget.Wasm64Wasi02 }
        select new object[] { mode, target };

    [Theory]
    [MemberData(nameof(ModesAndWidths))]
    public void ComponentAndRawSelectManagedAndRuntimeInputsBeforeMerge(
        FinalWasmOptimization optimization, ComponentTarget target)
    {
        var component = BrowserComponentCoreModules.CreateLinkPlan(
            new("application", "runtime", "output", target, Optimization: optimization),
            new("environment", "host", "adapter", "merged", "sanitized", "runtime-sanitized"));
        var raw = BrowserRawCoreModules.CreateLinkPlan(
            new RawModuleLinkRequest("application", "runtime", "output", target, optimization),
            new("temporary", "linked"));
        var expected = IsSpeedProfile(optimization)
            ? WasmInlineAnnotationSelection.Preserve
            : WasmInlineAnnotationSelection.SizeAware;
        // Managed AggressiveInlining hints also survive at Oz.
        var managedExpected = IsSpeedProfile(optimization) || optimization == FinalWasmOptimization.Oz
            ? WasmInlineAnnotationSelection.Preserve
            : WasmInlineAnnotationSelection.SizeAware;

        foreach (var (managedEdit, runtimeEdit, merge) in new[]
        {
            (component.Operations.OfType<BrowserLinkInlineOperation>().First(), component.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First(), component.Operations.OfType<BrowserLinkToolOperation>().First()),
            (raw.Operations.OfType<BrowserLinkInlineOperation>().First(), raw.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First(), raw.Operations.OfType<BrowserLinkToolOperation>().First()),
        })
        {
            Assert.Equal("application", managedEdit.InputPath);
            Assert.Equal(managedExpected, managedEdit.Selection);
            Assert.Equal("runtime", runtimeEdit.InputPath);
            Assert.Equal(expected, runtimeEdit.Selection);
            Assert.Equal(runtimeEdit.OutputPath, merge.Arguments[0]);
            Assert.Equal(managedEdit.OutputPath, merge.Arguments[2]);
            Assert.DoesNotContain("application", merge.Arguments);
            Assert.DoesNotContain("runtime", merge.Arguments);
        }
        Assert.Equal("sanitized", component.Operations.OfType<BrowserLinkInlineOperation>().First().OutputPath);
        Assert.Equal("runtime-sanitized", component.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First().OutputPath);
        Assert.Equal(Path.Combine("temporary", "sanitized.wasm"), raw.Operations.OfType<BrowserLinkInlineOperation>().First().OutputPath);
        Assert.Equal(Path.Combine("temporary", "runtime-sanitized.wasm"), raw.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First().OutputPath);
    }

    private static bool IsSpeedProfile(FinalWasmOptimization optimization) =>
        optimization is FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or
            FinalWasmOptimization.O3;
}
