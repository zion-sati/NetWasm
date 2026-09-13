using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedBoundaryPlanBuilderTests
{
    [Fact]
    public void RejectsMissingFailureDispositionPolicy()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedBoundaryPlanBuilder(null!));
    }

    [Fact]
    public void BuildsOneEntryThroughItsCapabilityContract()
    {
        var functions = new[]
        {
            new WasmFunctionDefinition("entry", WasmFunctionType.Create(CliValueKind.Void), []),
        };
        var entry = BuildEntry(
            new ManagedBoundaryPlanBuilder(new ManagedBoundaryFailurePolicy()),
            new(
                functions,
                2,
                2,
                "run",
                ManagedBoundaryKind.ProcessEntryPoint,
                IsOutwardFacing: true));

        Assert.Equal(2, entry.FunctionIndex);
        Assert.Equal("entry", entry.GeneratedFunctionName);
        Assert.Equal("run", entry.ExportName);
        Assert.Equal(ManagedBoundaryFailureDisposition.ReportAndTerminate, entry.Disposition);
    }

    private static readonly Func<IManagedBoundaryPlanBuilder,
        ManagedBoundaryPlanBuildRequest, ManagedBoundaryPlanEntry> BuildEntry =
        static (builder, request) => builder.Build(request);

    [Fact]
    public void BuildsAndValidatesAnEntryFromTheDefinedFunctionIndex()
    {
        var policy = new ManagedBoundaryFailurePolicy();
        var functions = new[]
        {
            new WasmFunctionDefinition("entry", WasmFunctionType.Create(CliValueKind.Void), []),
        };
        var entry = BuildEntry(
            new ManagedBoundaryPlanBuilder(policy),
            new(
                functions,
                2,
                2,
                "run",
                ManagedBoundaryKind.ProcessEntryPoint,
                IsOutwardFacing: true));

        new ManagedBoundaryPlanValidator(policy).Validate(
            functions,
            [new WasmExport("run", 2)],
            importedFunctionCount: 2,
            [entry]);
    }

    [Fact]
    public void RejectsAnIndexThatDoesNotIdentifyADefinedFunction()
    {
        var builder = new ManagedBoundaryPlanBuilder(
            new ManagedBoundaryFailurePolicy());

        Assert.Throws<InvalidOperationException>(() => BuildEntry(
            builder,
            new(
                [],
                1,
                0,
                "run",
                ManagedBoundaryKind.ProcessEntryPoint,
                IsOutwardFacing: true)));
    }
}
