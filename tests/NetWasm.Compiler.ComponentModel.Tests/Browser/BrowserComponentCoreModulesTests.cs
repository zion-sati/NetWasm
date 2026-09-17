using System.Collections.Immutable;
using System.Text;
using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserComponentCoreModulesTests
{
    private static readonly ImmutableArray<string> ExpectedCleanupPaths =
        ["v/./env.wasm", "v/host.wasm", "v/command.wasm", "v/merged.wasm", "v/sanitized.wasm"];
    private static readonly ImmutableArray<string> ExpectedManagedModulePaths =
        ["v/./env.wasm", "v/host.wasm", "v/command.wasm"];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawPlanPreservesExactVirtualPathsAndAuthoritativeReleaseArguments(bool memory64)
    {
        var target = memory64 ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02;
        var plan = BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(target) with { Optimization = FinalWasmOptimization.Size }, CreateWorkspace());
        var width = memory64 ? "i64" : "i32";

        Assert.Equal(new BrowserWasmTextModule("v/./env.wasm",
            $"(module (func (export \"emscripten_notify_memory_growth\") (param {width})))"),
            Assert.Single(plan.TextModules));
        var expectedMerge = new List<string>
        {
            "v/runtime.wasm", "netwasm.runtime.v1", "v/Application.wasm", "netwasm.application.v1",
            "v/./env.wasm", "env", "--output", "v/merged.wasm", "--enable-multimemory",
            "--enable-exception-handling", "--enable-bulk-memory", "--enable-nontrapping-float-to-int",
        };
        var expectedOptimization = new List<string>
        {
            "v/sanitized.wasm", "-Oz", "--remove-unused-module-elements", "--strip-debug",
            "--enable-multimemory", "--enable-exception-handling", "--enable-bulk-memory",
            "--enable-nontrapping-float-to-int",
        };
        if (memory64)
        {
            expectedMerge.Add("--enable-memory64");
            expectedOptimization.Add("--enable-memory64");
        }
        expectedOptimization.AddRange(["--output", "v/output.wasm"]);
        Assert.Equal(BinaryenToolIds.WasmMerge, plan.Merge.ToolId);
        Assert.Equal(expectedMerge, plan.Merge.Arguments);
        Assert.Equal(BinaryenToolIds.WasmOpt, plan.Optimization!.ToolId);
        Assert.Equal(expectedOptimization, plan.Optimization.Arguments);
        Assert.Null(plan.Validation);
        Assert.Null(plan.Copy);
        Assert.Equal(new BrowserComponentExportPruning("v/merged.wasm", "v/sanitized.wasm",
            memory64 ? "cm64p2" : "cm32p2"), plan.ExportPruning);
        Assert.Equal(ExpectedCleanupPaths, plan.CleanupPaths);
    }

    [Theory]
    [InlineData(false, ManagedExecutableReturnShape.Void)]
    [InlineData(false, ManagedExecutableReturnShape.ExitCode)]
    [InlineData(true, ManagedExecutableReturnShape.Void)]
    [InlineData(true, ManagedExecutableReturnShape.ExitCode)]
    public void ManagedPlanUsesAuthoritativeCompletionAdapter(bool asynchronous, ManagedExecutableReturnShape returns)
    {
        var abi = new ManagedExecutableEntryPointAbi(ManagedExecutableParameterShape.StringArray, returns,
            asynchronous ? ManagedExecutableCompletionShape.Asynchronous : ManagedExecutableCompletionShape.Synchronous);
        var plan = BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02) with { ManagedExecutableEntryPoint = abi }, CreateWorkspace());

        Assert.Equal(ExpectedManagedModulePaths, plan.TextModules.Select(module => module.OutputPath));
        Assert.Contains("report_terminal_exception_v1", plan.TextModules[1].Text);
        var adapter = plan.TextModules[2].Text;
        if (asynchronous)
        {
            Assert.Contains("netwasm:runtime/process@1|start", adapter);
            Assert.Contains("netwasm.process.status", adapter);
            Assert.Contains("netwasm.process.complete", adapter);
            Assert.DoesNotContain("wasi:cli/run", adapter);
            Assert.Equal(returns == ManagedExecutableReturnShape.ExitCode,
                adapter.Contains("netwasm.process.result", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains("cm32p2|wasi:cli/run@0.2|run", adapter);
            Assert.Contains(returns == ManagedExecutableReturnShape.ExitCode ? "i32.eqz i32.eqz" : "i32.const 0", adapter);
        }
        Assert.Equal(new[] { "v/host.wasm", RuntimeAbi.HostModule, "v/command.wasm", "netwasm.command.v1" },
            plan.Merge.Arguments.Skip(6).Take(4));
        Assert.Equal(5, plan.CleanupPaths.Length);
        // Workspace disposal has already run and cannot erase the captured values.
        Assert.NotEmpty(plan.TextModules[0].Text);
        Assert.NotEmpty(plan.Optimization!.Arguments);
    }

    [Fact]
    public void CaseDistinctVirtualPathsRemainDistinctAndNeitherIsNormalized()
    {
        var request = CreateRequest(ComponentTarget.Wasm32Wasi02) with { OutputPath = "v/application.wasm" };
        var plan = BrowserComponentCoreModules.CreateLinkPlan(request, CreateWorkspace());
        Assert.Contains("v/Application.wasm", plan.Merge.Arguments);
        Assert.Equal("v/application.wasm", plan.Optimization!.Arguments[^1]);
        Assert.Contains("v/./env.wasm", plan.CleanupPaths);
    }

    [Fact]
    public void NonePlanCopiesSanitizedCoreWithoutOptimizerInvocation()
    {
        var plan = BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02) with
            {
                Optimization = FinalWasmOptimization.None,
            }, CreateWorkspace());

        Assert.Null(plan.Optimization);
        Assert.Equal(new BrowserCoreModuleValidation("v/sanitized.wasm"), plan.Validation);
        Assert.Equal(new BrowserFileCopy("v/sanitized.wasm", "v/output.wasm"), plan.Copy);
    }

    [Fact]
    public void RejectsInputsThatAliasOwnedIntermediatesOrOutput()
    {
        Assert.Throws<ArgumentException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02) with { OutputPath = "v/runtime.wasm" }, CreateWorkspace()));
        Assert.Throws<ArgumentException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02), CreateWorkspace() with { MergedModulePath = "v/Application.wasm" }));
        Assert.Throws<ArgumentException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02), CreateWorkspace() with { SanitizedModulePath = "v/merged.wasm" }));
    }

    [Fact]
    public void RejectsBlankPathsAndMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => BrowserComponentCoreModules.CreateLinkPlan(null!, CreateWorkspace()));
        Assert.Throws<ArgumentNullException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02), null!));
        Assert.Throws<ArgumentNullException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(null!), CreateWorkspace()));
        Assert.Throws<ArgumentException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02) with { ApplicationModulePath = " " }, CreateWorkspace()));
        Assert.Throws<ArgumentException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02), CreateWorkspace() with { EnvironmentModulePath = " " }));
    }

    [Fact]
    public void AuthoritativeAdapterFailurePropagatesThroughCleanup()
    {
        var invalid = new ManagedExecutableEntryPointAbi(ManagedExecutableParameterShape.None,
            ManagedExecutableReturnShape.Void, (ManagedExecutableCompletionShape)99);
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02) with { ManagedExecutableEntryPoint = invalid }, CreateWorkspace()));
    }

    [Theory]
    [InlineData("cm32p2")]
    [InlineData("cm64p2")]
    public void PrunesBytesWithTheAuthoritativeExportPolicyAndPreservesOtherSections(string prefix)
    {
        var input = Module(prefix, extraMemory: false);
        var original = input.ToArray();
        var output = BrowserComponentCoreModules.RetainComponentExports(input, prefix);
        var expected = Module(prefix, extraMemory: false, includePrivateExports: false);

        Assert.Equal(expected, output);
        Assert.Equal(original, input);
        output[0] = 9;
        Assert.Equal(original, input);
        var repeated = BrowserComponentCoreModules.RetainComponentExports(input, prefix);
        Assert.Equal(expected, repeated);
    }

    [Fact]
    public void InvalidModuleAndMemoryShapePreserveCompilerDiagnosticsWithoutReturningOutput()
    {
        var malformed = Assert.Throws<CompilerException>(() =>
            BrowserComponentCoreModules.RetainComponentExports(new byte[] { 0, 97 }, "cm32p2"));
        Assert.Equal(DiagnosticCode.ComponentContract, malformed.Diagnostic.Code);
        Assert.Throws<CompilerException>(() => BrowserComponentCoreModules.RetainComponentExports(
            Module("cm64p2", extraMemory: false), "cm32p2"));
        Assert.Throws<CompilerException>(() => BrowserComponentCoreModules.RetainComponentExports(
            Module("cm32p2", extraMemory: true), "cm32p2"));
        Assert.Throws<ArgumentException>(() => BrowserComponentCoreModules.RetainComponentExports(
            Module("cm32p2", extraMemory: false), " "));
    }

    private static ComponentCoreModuleLinkRequest CreateRequest(ComponentTarget target) =>
        new("v/Application.wasm", "v/runtime.wasm", "v/output.wasm", target);

    private static BrowserComponentCoreModuleWorkspace CreateWorkspace() =>
        new("v/./env.wasm", "v/host.wasm", "v/command.wasm", "v/merged.wasm", "v/sanitized.wasm");

    private static byte[] Module(string prefix, bool extraMemory, bool includePrivateExports = true)
    {
        var exports = new List<byte[]> { Export(prefix + "_memory", 2), Export(prefix + "|world|run", 0) };
        if (extraMemory) exports.Add(Export(prefix + "_memory", 2));
        if (includePrivateExports) exports.AddRange([Export("run", 0), Export("memory", 2)]);
        var payload = new byte[] { (byte)exports.Count }.Concat(exports.SelectMany(export => export)).ToArray();
        return [0, 97, 115, 109, 1, 0, 0, 0, 0, 3, 1, 120, 42, 7, (byte)payload.Length, .. payload];
    }

    private static byte[] Export(string name, byte kind)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        return [(byte)bytes.Length, .. bytes, kind, 0];
    }
}
