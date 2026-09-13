using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class AsyncJSImportHelperAppenderTests
{
    [Fact]
    public void AppendsResolveRejectCancelIndicesAndBoundaryPolicies()
    {
        var binding = CreateBinding();
        var functions = new List<WasmFunctionDefinition>();
        var indices = ImmutableDictionary.CreateBuilder<string, int>();
        var boundaryEntries = new List<ManagedBoundaryPlanEntry>();
        var boundaryBuilder = new RecordingBoundaryBuilder();
        IAsyncJSImportHelperAppender appender = new[]
        {
            new AsyncJSImportHelperAppender(
                new FixedResolveEmitter(),
                new FixedRejectEmitter(),
                new FixedCancelEmitter(),
                new FixedResolveTypeResolver(),
                boundaryBuilder),
        }.Cast<IAsyncJSImportHelperAppender>().Single();

        appender.Append(
            functions,
            4,
            indices,
            binding,
            new FixedFunctionIndexResolver(),
            boundaryEntries);

        Assert.Equal(3, functions.Count);
        Assert.Equal(3, indices.Count);
        Assert.Equal(3, boundaryEntries.Count);
        Assert.All(boundaryEntries, entry => Assert.Equal(
            ManagedBoundaryKind.AsynchronousImportCompletion,
            entry.Kind));
        Assert.Equal([4, 5, 6], boundaryEntries.Select(entry => entry.FunctionIndex));
    }

    [Fact]
    public void RejectsMissingInputsAndNegativeImportCount()
    {
        var appender = CreateAppender();
        var functions = new List<WasmFunctionDefinition>();
        var indices = ImmutableDictionary.CreateBuilder<string, int>();
        var binding = CreateBinding();
        var functionIndices = new FixedFunctionIndexResolver();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        Assert.Throws<ArgumentNullException>(() => appender.Append(
            null!, 0, indices, binding, functionIndices, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, -1, indices, binding, functionIndices, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, null!, binding, functionIndices, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, null!, functionIndices, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, binding, null!, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, binding, functionIndices, null!));
    }

    private static IAsyncJSImportHelperAppender CreateAppender() => new[]
    {
        new AsyncJSImportHelperAppender(
            new FixedResolveEmitter(),
            new FixedRejectEmitter(),
            new FixedCancelEmitter(),
            new FixedResolveTypeResolver(),
            new RecordingBoundaryBuilder()),
    }.Cast<IAsyncJSImportHelperAppender>().Single();

    private static JavaScriptAsyncMethodBinding CreateBinding()
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
            new(JavaScriptAsyncReturnKind.Task, null),
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "System.Threading.Tasks",
                "Task",
                isValueType: false),
            instance,
            instance,
            instance);
    }

    private sealed class FixedResolveEmitter : IAsyncJSImportResolveEmitter
    {
        public byte[] Emit(
            JavaScriptAsyncMethodBinding binding,
            IFunctionIndexResolver functionIndices) => [1];
    }

    private sealed class FixedRejectEmitter : IAsyncJSImportRejectEmitter
    {
        public byte[] Emit(
            JavaScriptAsyncMethodBinding binding,
            IFunctionIndexResolver functionIndices) => [2];
    }

    private sealed class FixedCancelEmitter : IAsyncJSImportCancelEmitter
    {
        public byte[] Emit(
            JavaScriptAsyncMethodBinding binding,
            IFunctionIndexResolver functionIndices) => [3];
    }

    private sealed class FixedResolveTypeResolver : IAsyncJSImportResolveTypeResolver
    {
        public WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding) =>
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4);
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public int Resolve(EntityKey method) => 0;
        public int Resolve(string method) => 0;
        public int Resolve(MethodInstanceModel method) => 0;
    }

    private sealed class RecordingBoundaryBuilder : IManagedBoundaryPlanBuilder
    {
        public ManagedBoundaryPlanEntry Build(ManagedBoundaryPlanBuildRequest request) =>
            new(
                request.FunctionIndex,
                request.Functions[request.FunctionIndex - request.ImportedFunctionCount].Name,
                request.ExportName,
                request.Kind,
                request.IsOutwardFacing,
                ManagedBoundaryFailureDisposition.PropagateManagedException);
    }
}
