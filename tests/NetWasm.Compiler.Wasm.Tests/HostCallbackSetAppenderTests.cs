using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class HostCallbackSetAppenderTests
{
    [Fact]
    public void AppendsCallbacksInOrdinalExportOrder()
    {
        var leaf = new RecordingHostCallbackAppender();
        var appender = CreateAppender(leaf);
        var program = new FakeProgram();
        var target = EmitterTestSupport.CreateInstructionModuleTarget(program);
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        appender.Append(functions, 3, indices,
            [CreateCallback(program, "zeta"), CreateCallback(program, "alpha")],
            target.FunctionIndices, TestRuntimeInitialization.Create(256), target.InteropImports, CoreImports, boundaries);

        Assert.Equal(["alpha", "zeta"], leaf.ExportNames);
        Assert.All(leaf.ImportCounts, count => Assert.Equal(3, count));
        Assert.All(leaf.StaticDataEnds, value => Assert.Equal(256, value));
    }

    [Fact]
    public void SupportsEmptySetAndRejectsInvalidCollaborativeInputs()
    {
        var appender = CreateAppender(new RecordingHostCallbackAppender());
        var target = EmitterTestSupport.CreateInstructionModuleTarget();
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        appender.Append(functions, 0, indices, [], target.FunctionIndices, TestRuntimeInitialization.Create(0),
            target.InteropImports, CoreImports, boundaries);
        Assert.Throws<ArgumentNullException>(() => appender.Append(null!, 0, indices, [],
            target.FunctionIndices, TestRuntimeInitialization.Create(0), target.InteropImports, CoreImports, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(functions, -1,
            indices, [], target.FunctionIndices, TestRuntimeInitialization.Create(0), target.InteropImports, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, null!, [],
            target.FunctionIndices, TestRuntimeInitialization.Create(0), target.InteropImports, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices, [],
            null!, TestRuntimeInitialization.Create(0), target.InteropImports, CoreImports, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(functions, 0,
            indices, [], target.FunctionIndices, TestRuntimeInitialization.Create(-1), target.InteropImports, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices, [],
            target.FunctionIndices, TestRuntimeInitialization.Create(0), null!, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(functions, 0, indices, [],
            target.FunctionIndices, TestRuntimeInitialization.Create(0), target.InteropImports, CoreImports, null!));
    }

    private static IHostCallbackSetAppender CreateAppender(
        IHostCallbackFunctionAppender leaf) => new[]
    {
        new HostCallbackSetAppender(leaf),
    }.Cast<IHostCallbackSetAppender>().Single();

    private static RuntimeImportSelection CoreImports =>
        new(WasmModuleProfile.CoreApplication, true);

    private static HostCallbackDeclaration CreateCallback(
        FakeProgram program,
        string exportName)
    {
        var method = program.GetMethod(EmitterTestSupport.EntryKey);
        var instance = new MethodInstanceModel(method,
            CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "Callback",
                isValueType: false), [], method.Signature);
        return new(method.Key, 0, instance, exportName);
    }

    private sealed class RecordingHostCallbackAppender :
        IHostCallbackFunctionAppender
    {
        public List<string> ExportNames { get; } = [];
        public List<int> ImportCounts { get; } = [];
        public List<int> StaticDataEnds { get; } = [];

        public void Append(IList<WasmFunctionDefinition> functions, int importCount,
            IDictionary<string, int> callbackIndices,
            HostCallbackDeclaration callback, FunctionIndexMap functionIndices,
            RuntimeInitializationPlan initialization,
            InteropImportPlan interopImports,
            RuntimeImportSelection runtimeImportSelection,
            ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
        {
            ExportNames.Add(callback.ExportName);
            ImportCounts.Add(importCount);
            StaticDataEnds.Add(initialization.StaticDataEnd);
        }
    }
}
