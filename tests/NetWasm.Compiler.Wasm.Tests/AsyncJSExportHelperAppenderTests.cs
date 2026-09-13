using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class AsyncJSExportHelperAppenderTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void AppendsRequiredHelpersIndicesAndBoundaryPolicies(
        bool hasResult,
        int expectedCount)
    {
        var binding = CreateBinding(hasResult);
        var functions = new List<WasmFunctionDefinition>();
        var indices = ImmutableDictionary.CreateBuilder<string, int>();
        var boundaries = new List<ManagedBoundaryPlanEntry>();
        var boundaryBuilder = new RecordingBoundaryBuilder();
        IAsyncJSExportHelperAppender appender = new[]
        {
            new AsyncJSExportHelperAppender(
                new FixedStatusEmitter(),
                new FixedResultEmitter(),
                new FixedCompletionEmitter(),
                new FixedResultTypeResolver(),
                boundaryBuilder),
        }.Cast<IAsyncJSExportHelperAppender>().Single();

        appender.Append(
            functions,
            5,
            indices,
            binding,
            ManagedAsyncBoundaryNames.ForExport(binding),
            ManagedAsyncBoundaryKinds.Export,
            boundaries);

        Assert.Equal(expectedCount, functions.Count);
        Assert.Equal(expectedCount, indices.Count);
        Assert.Equal(expectedCount, boundaries.Count);
        Assert.Equal(expectedCount, boundaryBuilder.Requests.Count);
        Assert.Equal(5, boundaries[0].FunctionIndex);
        Assert.Equal(
            ManagedBoundaryKind.AsynchronousExportCompletion,
            boundaries[^1].Kind);
        Assert.Equal(hasResult, indices.ContainsKey(
            JavaScriptAsyncAbiNames.ExportResult(binding.Method)));
    }

    [Fact]
    public void RejectsMissingInputsAndNegativeImportCount()
    {
        var appender = CreateAppender();
        var functions = new List<WasmFunctionDefinition>();
        var indices = ImmutableDictionary.CreateBuilder<string, int>();
        var binding = CreateBinding(false);
        var names = ManagedAsyncBoundaryNames.ForExport(binding);
        var kinds = ManagedAsyncBoundaryKinds.Export;
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        Assert.Throws<ArgumentNullException>(() =>
            appender.Append(null!, 0, indices, binding, names, kinds, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            appender.Append(functions, -1, indices, binding, names, kinds, boundaries));
        Assert.Throws<ArgumentNullException>(() =>
            appender.Append(functions, 0, null!, binding, names, kinds, boundaries));
        Assert.Throws<ArgumentNullException>(() =>
            appender.Append(functions, 0, indices, null!, names, kinds, boundaries));
        Assert.Throws<ArgumentNullException>(() =>
            appender.Append(functions, 0, indices, binding, null!, kinds, boundaries));
        Assert.Throws<ArgumentNullException>(() =>
            appender.Append(functions, 0, indices, binding, names, null!, boundaries));
        Assert.Throws<ArgumentNullException>(() =>
            appender.Append(functions, 0, indices, binding, names, kinds, null!));
    }

    [Fact]
    public void RejectsAMissingResultNameForAResultBearingTask()
    {
        var appender = CreateAppender();
        var binding = CreateBinding(true);

        Assert.Throws<InvalidOperationException>(() => appender.Append(
            [],
            0,
            new Dictionary<string, int>(),
            binding,
            new(
                JavaScriptAsyncAbiNames.ExportStatus(binding.Method),
                null,
                JavaScriptAsyncAbiNames.ExportComplete(binding.Method)),
            ManagedAsyncBoundaryKinds.Export,
            []));
    }

    private static IAsyncJSExportHelperAppender CreateAppender() => new[]
    {
        new AsyncJSExportHelperAppender(
            new FixedStatusEmitter(),
            new FixedResultEmitter(),
            new FixedCompletionEmitter(),
            new FixedResultTypeResolver(),
            new RecordingBoundaryBuilder()),
    }.Cast<IAsyncJSExportHelperAppender>().Single();

    private static JavaScriptAsyncMethodBinding CreateBinding(bool hasResult)
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EmitterTestSupport.EntryKey);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "TaskSource",
                isValueType: false),
            [],
            method.Signature);
        return new(
            EmitterTestSupport.EntryKey,
            new(
                JavaScriptAsyncReturnKind.Task,
                hasResult
                    ? CliTypeIdentity.FromStackKind(CliValueKind.I4)
                    : null),
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "System.Threading.Tasks",
                "Task",
                isValueType: false),
            instance,
            instance,
            instance);
    }

    private sealed class FixedStatusEmitter : IAsyncJSExportStatusEmitter
    {
        public byte[] Emit(JavaScriptAsyncMethodBinding binding) => [1];
    }

    private sealed class FixedResultEmitter : IAsyncJSExportResultEmitter
    {
        public byte[] Emit(JavaScriptAsyncMethodBinding binding) => [2];
    }

    private sealed class FixedCompletionEmitter : IAsyncJSExportCompletionEmitter
    {
        public byte[] Emit() => [3];
    }

    private sealed class FixedResultTypeResolver : IAsyncJSExportResultTypeResolver
    {
        public WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding) =>
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4);
    }

    private sealed class RecordingBoundaryBuilder : IManagedBoundaryPlanBuilder
    {
        public List<ManagedBoundaryPlanBuildRequest> Requests { get; } = [];

        public ManagedBoundaryPlanEntry Build(ManagedBoundaryPlanBuildRequest request)
        {
            Requests.Add(request);
            return new(
                request.FunctionIndex,
                request.Functions[request.FunctionIndex - request.ImportedFunctionCount].Name,
                request.ExportName,
                request.Kind,
                request.IsOutwardFacing,
                ManagedBoundaryFailureDisposition.PropagateManagedException);
        }
    }
}
