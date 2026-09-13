using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedBoundaryPlanValidatorTests
{
    [Fact]
    public void RequiresEveryActualFunctionExportToHaveExactlyOneAssignment()
    {
        var functions = new[]
        {
            new WasmFunctionDefinition("entry.body", WasmFunctionType.Create(CliValueKind.Void), []),
            new WasmFunctionDefinition("dispatch.body", WasmFunctionType.Create(CliValueKind.Void), []),
        };
        var exports = new[] { new WasmExport("run", 3), new WasmExport("dispatch", 4) };

        Action<IManagedBoundaryPlanValidator> contract = validator => validator.Validate(
            functions, exports, 3,
        [
            new(3, "entry.body", "run", ManagedBoundaryKind.ProcessEntryPoint, true, ManagedBoundaryFailureDisposition.ReportAndTerminate),
            new(4, "dispatch.body", "dispatch", ManagedBoundaryKind.InternalRuntimeDispatch, false, ManagedBoundaryFailureDisposition.PropagateManagedException),
        ]);
        contract(new ManagedBoundaryPlanValidator(new ManagedBoundaryFailurePolicy()));
    }

    [Fact]
    public void AcceptsExactlyOnceAssignmentsThatMatchPolicy()
    {
        var validator = new ManagedBoundaryPlanValidator(new ManagedBoundaryFailurePolicy());

        validator.Validate(
        [
            new WasmFunctionDefinition("entry", WasmFunctionType.Create(CliValueKind.Void), []),
            new WasmFunctionDefinition("dispatch", WasmFunctionType.Create(CliValueKind.Void), []),
        ],
        [new WasmExport("entry", 0), new WasmExport("dispatch", 1)],
        0,
        [
            new(0, "entry", "entry", ManagedBoundaryKind.ProcessEntryPoint, true, ManagedBoundaryFailureDisposition.ReportAndTerminate),
            new(1, "dispatch", "dispatch", ManagedBoundaryKind.InternalRuntimeDispatch, false, ManagedBoundaryFailureDisposition.PropagateManagedException),
            ]);
    }

    [Fact]
    public void AcceptsMultipleExportAliasesForOneClassifiedFunction()
    {
        var validator = new ManagedBoundaryPlanValidator(new ManagedBoundaryFailurePolicy());

        validator.Validate(
            [new WasmFunctionDefinition("entry", WasmFunctionType.Create(CliValueKind.Void), [])],
            [new WasmExport("primary", 0), new WasmExport("alias", 0)],
            0,
            [
                new(
                    0,
                    "entry",
                    "primary",
                    ManagedBoundaryKind.ProcessEntryPoint,
                    true,
                    ManagedBoundaryFailureDisposition.ReportAndTerminate),
            ]);
    }

    [Fact]
    public void RejectsDuplicateAssignments()
    {
        var validator = new ManagedBoundaryPlanValidator(new ManagedBoundaryFailurePolicy());

        var error = Assert.Throws<InvalidOperationException>(() => validator.Validate(
        [new WasmFunctionDefinition("entry", WasmFunctionType.Create(CliValueKind.Void), [])],
        [new WasmExport("entry", 0)],
        0,
        [
            new(0, "entry", "entry", ManagedBoundaryKind.ProcessEntryPoint, true, ManagedBoundaryFailureDisposition.ReportAndTerminate),
            new(0, "entry", "entry", ManagedBoundaryKind.ProcessEntryPoint, true, ManagedBoundaryFailureDisposition.ReportAndTerminate),
        ]));

        Assert.Contains("more than one", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTerminalReportingFromInternalHelpers()
    {
        var validator = new ManagedBoundaryPlanValidator(new ManagedBoundaryFailurePolicy());

        var error = Assert.Throws<InvalidOperationException>(() => validator.Validate(
        [new WasmFunctionDefinition("helper", WasmFunctionType.Create(CliValueKind.Void), [])],
        [new WasmExport("helper", 0)],
        0,
        [
            new(0, "helper", "helper", ManagedBoundaryKind.ProcessEntryPoint, false, ManagedBoundaryFailureDisposition.ReportAndTerminate),
        ]));

        Assert.Contains("cannot own terminal reporting", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingPolicyDependency()
    {
        Assert.Throws<ArgumentNullException>(() => new ManagedBoundaryPlanValidator(null!));
    }

    [Fact]
    public void RejectsNullInputsThroughItsContract()
    {
        var validator = CreateValidator();
        var functions = Array.Empty<WasmFunctionDefinition>();
        var exports = Array.Empty<WasmExport>();
        var entries = Array.Empty<ManagedBoundaryPlanEntry>();

        Assert.Throws<ArgumentNullException>(() =>
            validator.Validate(null!, exports, 0, entries));
        Assert.Throws<ArgumentNullException>(() =>
            validator.Validate(functions, null!, 0, entries));
        Assert.Throws<ArgumentNullException>(() =>
            validator.Validate(functions, exports, 0, null!));
    }

    [Theory]
    [InlineData("omitted", "no managed-boundary policy assignment")]
    [InlineData("unexported", "is not exported")]
    [InlineData("blank-name", "stable function name")]
    [InlineData("import-index", "invalid function index")]
    [InlineData("upper-index", "invalid function index")]
    [InlineData("name-mismatch", "does not identify that generated function")]
    [InlineData("export-mismatch", "does not identify its actual export")]
    [InlineData("disposition", "expected")]
    public void RejectsEveryInvalidPlanInvariant(string scenario, string message)
    {
        var functions = new[]
        {
            new WasmFunctionDefinition(
                "entry",
                WasmFunctionType.Create(CliValueKind.Void),
                []),
        };
        var exports = new[] { new WasmExport("entry", 1) };
        var entries = new[]
        {
            new ManagedBoundaryPlanEntry(
                1,
                "entry",
                "entry",
                ManagedBoundaryKind.ProcessEntryPoint,
                true,
                ManagedBoundaryFailureDisposition.ReportAndTerminate),
        };
        var importedFunctionCount = 1;

        switch (scenario)
        {
            case "omitted":
                entries = [];
                break;
            case "unexported":
                exports = [];
                break;
            case "blank-name":
                entries = [entries[0] with { GeneratedFunctionName = " " }];
                break;
            case "import-index":
                exports = [new WasmExport("entry", 0)];
                entries = [entries[0] with { FunctionIndex = 0 }];
                break;
            case "upper-index":
                exports = [new WasmExport("entry", 2)];
                entries = [entries[0] with { FunctionIndex = 2 }];
                break;
            case "name-mismatch":
                entries = [entries[0] with { GeneratedFunctionName = "other" }];
                break;
            case "export-mismatch":
                entries = [entries[0] with { ExportName = "other" }];
                break;
            case "disposition":
                entries =
                [
                    entries[0] with
                    {
                        Disposition = ManagedBoundaryFailureDisposition.PropagateManagedException,
                    },
                ];
                break;
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate(
                functions,
                exports,
                importedFunctionCount,
                entries));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private static IManagedBoundaryPlanValidator CreateValidator() => new[]
    {
        new ManagedBoundaryPlanValidator(new ManagedBoundaryFailurePolicy()),
    }.Cast<IManagedBoundaryPlanValidator>().Single();
}
