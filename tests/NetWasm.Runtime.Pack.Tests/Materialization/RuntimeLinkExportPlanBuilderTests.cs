using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeLinkExportPlanBuilderTests
{
    [Fact]
    public void KeepsPublicRuntimeExportsOutOfTheNativeRemovalPlan()
    {
        var capability = Assert.IsAssignableFrom<IRuntimeLinkExportPlanBuilder>(new RuntimeLinkExportPlanBuilder());
        var plan = capability.Build(new(["initialize", "native", "__heap_base"],
            [Binding("native"), Binding("other"), Binding("other"), Binding("emscripten_stack_get_current"), Binding("__start_em_asm")]));
        Assert.Contains("--export=native", plan.Arguments);
        Assert.Contains("--export=other", plan.Arguments);
        Assert.Contains("--export-if-defined=__start_em_asm", plan.Arguments);
        Assert.Equal([new("__global_base", 3), new("__data_end", 3), new("__stack_low", 3), new("__stack_high", 3),
            new RuntimeLinkExport("other", 0)], plan.InternalExports.ToArray());
    }

    [Fact]
    public void NonNativePlanPreservesExistingExportOrderWithoutTemporaryRoots()
    {
        var plan = new RuntimeLinkExportPlanBuilder().Build(new(["initialize", "allocate"], [])
        {
            RuntimeFeatures = [],
        });
        Assert.Equal(["--export=emscripten_stack_get_current", "--export=_emscripten_stack_restore",
            "--export-if-defined=__start_em_asm", "--export-if-defined=__stop_em_asm",
            "--export-if-defined=__start_em_lib_deps", "--export-if-defined=__stop_em_lib_deps",
            "--export-if-defined=__start_em_js", "--export-if-defined=__stop_em_js",
            "--export=initialize", "--export=allocate"], plan.Arguments.ToArray());
        Assert.Empty(plan.InternalExports);
    }

    [Fact]
    public void SelectsOptionalExportsFromCompilerRuntimeFeatures()
    {
        var builder = new RuntimeLinkExportPlanBuilder();
        var manifestExports = ImmutableArray.Create(
            "initialize",
            "ephemeron_handle_get_key",
            "ephemeron_handle_get_value",
            "ephemeron_handle_new",
            "ephemeron_handle_release");

        var ordinary = builder.Build(new(manifestExports, [])
        {
            RuntimeFeatures = [],
        });
        Assert.DoesNotContain(ordinary.Arguments,
            argument => argument.Contains("ephemeron_handle", StringComparison.Ordinal));
        Assert.DoesNotContain(ordinary.Arguments,
            argument => argument.Contains("command_exception", StringComparison.Ordinal));

        var ephemerons = builder.Build(new(manifestExports, [])
        {
            RuntimeFeatures = ["ephemeron-handles"],
        });
        Assert.Contains("--export=ephemeron_handle_new", ephemerons.Arguments);
        Assert.DoesNotContain(ephemerons.Arguments,
            argument => argument.Contains("command_exception", StringComparison.Ordinal));

        var diagnostics = builder.Build(new(manifestExports, [])
        {
            RuntimeFeatures = ["structured-command-diagnostics"],
        });
        Assert.DoesNotContain(diagnostics.Arguments,
            argument => argument.Contains("ephemeron_handle", StringComparison.Ordinal));
        Assert.Contains("--export=command_exception_capture", diagnostics.Arguments);
        Assert.Contains("--export=command_exception_completion", diagnostics.Arguments);
        Assert.Contains("--export=command_exception_release", diagnostics.Arguments);
        Assert.Contains("--export=command_exception_write", diagnostics.Arguments);

        var legacy = builder.Build(new(manifestExports, []));
        Assert.Contains("--export=ephemeron_handle_new", legacy.Arguments);
        Assert.Contains("--export=command_exception_capture", legacy.Arguments);
    }

    [Fact]
    public void CallbackOnlyPlanRootsGetterAndMarksItForRemoval()
    {
        var support = RuntimePackTestData.CallbackSupport();

        var plan = new RuntimeLinkExportPlanBuilder().Build(new(["initialize"], [])
        {
            NativeCallbackSupport = support,
        });

        Assert.Contains("--export=__netwasm_callback_address_0", plan.Arguments);
        Assert.Contains(new RuntimeLinkExport("__netwasm_callback_address_0", 0),
            plan.InternalExports);
        Assert.Contains(new RuntimeLinkExport("__global_base", 3),
            plan.InternalExports);
    }

    [Fact]
    public void RejectsInvalidOrContradictoryFacts()
    {
        var builder = new RuntimeLinkExportPlanBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        foreach (var request in new RuntimeLinkExportPlanRequest[]
        {
            new(default, []), new([], default), new([null!], []), new(["\n"], []),
            new([], [null!]), new([], [Binding("native") with { Import = null! }]),
            new([], [Binding("")]), new([], [Binding("__heap_base")]),
        }) Assert.Throws<InvalidOperationException>(() => builder.Build(request));
    }

    [Fact]
    public void RejectsCallbackGetterCollisionsBeforeBuildingArguments()
    {
        var builder = new RuntimeLinkExportPlanBuilder();
        var support = RuntimePackTestData.CallbackSupport();

        Assert.Throws<InvalidOperationException>(() => builder.Build(
            new(["initialize"], [])
            {
                NativeCallbackSupport = support with
                {
                    TemporaryRuntimeExports = ["initialize"],
                },
            }));
        Assert.Throws<InvalidOperationException>(() => builder.Build(
            new([], [Binding("native")])
            {
                NativeCallbackSupport = support with
                {
                    TemporaryRuntimeExports = ["native"],
                },
            }));
    }

    private static RuntimeNativeBinding Binding(string name) =>
        new(new("mule", name, ImmutableArray<RuntimeNativeValueType>.Empty, null), new("mule", "wasm32", "/native.a", new string('a', 64)));
}
