using System.Collections.Immutable;
using System.Text;
using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserComponentCoreModulesTests
{
    private static readonly ImmutableArray<string> ExpectedCleanupPaths =
        ["v/env.wasm", "v/host.wasm", "v/command.wasm", "v/merged.wasm", "v/sanitized.wasm", "v/runtime-sanitized.wasm"];
    private static readonly ImmutableArray<string> ExpectedManagedModulePaths =
        ["v/env.wasm", "v/host.wasm", "v/command.wasm"];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawPlanPreservesExactVirtualPathsAndAuthoritativeReleaseArguments(bool memory64)
    {
        var target = memory64 ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02;
        var plan = BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(target) with { Optimization = FinalWasmOptimization.Oz }, CreateWorkspace());
        var width = memory64 ? "i64" : "i32";

        Assert.Equal(new BrowserLinkTextOperation("v/env.wasm",
            $"(module (func (export \"emscripten_notify_memory_growth\") (param {width})))"),
            Assert.Single(plan.Operations.OfType<BrowserLinkTextOperation>().ToArray()));
        var expectedMerge = new List<string>
        {
            "v/runtime-sanitized.wasm", "netwasm.runtime.v1", "v/sanitized.wasm", "netwasm.application.v1",
            "v/env.wasm", "env", "--output", "v/merged.wasm", "--enable-multimemory",
            "--enable-exception-handling", "--enable-reference-types", "--enable-bulk-memory", "--enable-nontrapping-float-to-int",
            "--enable-simd",
        };
        var expectedOptimization = new List<string>
        {
            "v/sanitized.wasm", "-Oz", "--converge", "--remove-unused-module-elements", "--strip-debug",
            "--enable-multimemory", "--enable-exception-handling", "--enable-reference-types", "--enable-bulk-memory",
            "--enable-nontrapping-float-to-int", "--enable-simd",
        };
        if (memory64)
        {
            expectedMerge.Add("--enable-memory64");
            expectedOptimization.Add("--enable-memory64");
        }
        expectedOptimization.AddRange(["--output", "v/output.wasm"]);
        Assert.Equal(BinaryenToolIds.WasmMerge, plan.Operations.OfType<BrowserLinkToolOperation>().First().ToolId);
        Assert.Equal(expectedMerge, plan.Operations.OfType<BrowserLinkToolOperation>().First().Arguments);
        Assert.Equal(new BrowserLinkInlineOperation("v/Application.wasm", "v/sanitized.wasm",
            WasmInlineAnnotationSelection.Preserve), plan.Operations.OfType<BrowserLinkInlineOperation>().First());
        Assert.Equal(new BrowserLinkInlineOperation("v/runtime.wasm", "v/runtime-sanitized.wasm",
            WasmInlineAnnotationSelection.SizeAware), plan.Operations.OfType<BrowserLinkInlineOperation>().Skip(1).First());
        Assert.Equal(BinaryenToolIds.WasmOpt, plan.Operations.OfType<BrowserLinkToolOperation>().Skip(1).SingleOrDefault()!.ToolId);
        Assert.Equal(expectedOptimization, plan.Operations.OfType<BrowserLinkToolOperation>().Skip(1).Single().Arguments);
        Assert.Equal("v/output.wasm", plan.Operations.OfType<BrowserLinkValidationOperation>().Single()!.Path);
        Assert.Equal(ExpectedValidation("v/output.wasm", memory64), plan.Operations.OfType<BrowserLinkValidationOperation>().Single().Arguments);
        Assert.Equal(new BrowserLinkInlineOperation("v/output.wasm", "v/output.wasm",
            WasmInlineAnnotationSelection.Remove), plan.Operations.OfType<BrowserLinkInlineOperation>().Last());
        Assert.Equal(new BrowserLinkExportOperation("v/merged.wasm", "v/sanitized.wasm",
            memory64 ? "cm64p2" : "cm32p2", []), plan.Operations.OfType<BrowserLinkExportOperation>().SingleOrDefault());
        Assert.Equal(ExpectedCleanupPaths, plan.TemporaryPaths);
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

        Assert.Equal(ExpectedManagedModulePaths, plan.Operations.OfType<BrowserLinkTextOperation>().ToArray().Select(module => module.OutputPath));
        Assert.Contains("report_terminal_exception_v2", plan.Operations.OfType<BrowserLinkTextOperation>().ToArray()[1].Text);
        var adapter = plan.Operations.OfType<BrowserLinkTextOperation>().ToArray()[2].Text;
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
            plan.Operations.OfType<BrowserLinkToolOperation>().First().Arguments.Skip(6).Take(4));
        Assert.Equal(6, plan.TemporaryPaths.Length);
        // Workspace disposal has already run and cannot erase the captured values.
        Assert.NotEmpty(plan.Operations.OfType<BrowserLinkTextOperation>().ToArray()[0].Text);
        Assert.NotEmpty(plan.Operations.OfType<BrowserLinkToolOperation>().Skip(1).SingleOrDefault()!.Arguments);
    }

    [Fact]
    public void CaseDistinctVirtualPathsRemainDistinct()
    {
        var request = CreateRequest(ComponentTarget.Wasm32Wasi02) with { OutputPath = "v/application.wasm" };
        var plan = BrowserComponentCoreModules.CreateLinkPlan(request, CreateWorkspace());
        Assert.Equal("v/Application.wasm", plan.Operations.OfType<BrowserLinkInlineOperation>().First().InputPath);
        Assert.Contains(plan.Operations.OfType<BrowserLinkInlineOperation>().First().OutputPath, plan.Operations.OfType<BrowserLinkToolOperation>().First().Arguments);
        Assert.Equal("v/application.wasm", plan.Operations.OfType<BrowserLinkToolOperation>().Skip(1).SingleOrDefault()!.Arguments[^1]);
        Assert.Contains("v/env.wasm", plan.TemporaryPaths);
    }

    [Fact]
    public void NonePlanCopiesSanitizedCoreWithoutOptimizerInvocation()
    {
        var plan = BrowserComponentCoreModules.CreateLinkPlan(
            CreateRequest(ComponentTarget.Wasm32Wasi02) with
            {
                Optimization = FinalWasmOptimization.None,
            }, CreateWorkspace());

        Assert.Null(plan.Operations.OfType<BrowserLinkToolOperation>().Skip(1).SingleOrDefault());
        Assert.Equal("v/sanitized.wasm", plan.Operations.OfType<BrowserLinkValidationOperation>().Single()!.Path);
        Assert.Equal(ExpectedValidation("v/sanitized.wasm", false), plan.Operations.OfType<BrowserLinkValidationOperation>().Single().Arguments);
        Assert.Equal(new BrowserLinkInlineOperation("v/sanitized.wasm", "v/output.wasm",
            WasmInlineAnnotationSelection.Remove), plan.Operations.OfType<BrowserLinkInlineOperation>().Last());
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

    private static string[] ExpectedValidation(string path, bool memory64) =>
        ["validate", path, "--features",
            "mvp,mutable-global,saturating-float-to-int,sign-extension,reference-types," +
            "multi-value,bulk-memory,exceptions,multi-memory,simd" + (memory64 ? ",memory64" : "")];

    private static ComponentCoreModuleLinkRequest CreateRequest(ComponentTarget target) =>
        new("v/Application.wasm", "v/runtime.wasm", "v/output.wasm", target);

    private static BrowserComponentCoreModuleWorkspace CreateWorkspace() =>
        new("v/env.wasm", "v/host.wasm", "v/command.wasm", "v/merged.wasm", "v/sanitized.wasm",
            "v/runtime-sanitized.wasm");

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
