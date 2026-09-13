using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeFunctionAppenderTests
{
    [Theory]
    [InlineData(WasmEntryPointProfile.Process, true, 3)]
    [InlineData(WasmEntryPointProfile.Internal, false, 2)]
    public void AppendsRuntimeDispatchersAndEntryPointSpecificBoundary(
        WasmEntryPointProfile entryPointProfile,
        bool hasFinalizers,
        int expectedBoundaryCount)
    {
        var descriptors = new FixedDescriptorSource(hasFinalizers);
        var finalizers = new RecordingFinalizerEmitter();
        var entries = new RecordingEntryPointEmitter();
        var functionTypes = new FixedFunctionTypeResolver();
        var boundariesBuilder = new FixedBoundaryBuilder();
        IRuntimeFunctionAppender appender = new[]
        {
            new RuntimeFunctionAppender(
                descriptors,
                new FixedFilterDispatcherEmitter(),
                finalizers,
                entries,
                functionTypes,
                CreateOutward(entries, functionTypes, boundariesBuilder),
                boundariesBuilder),
        }.Cast<IRuntimeFunctionAppender>().Single();
        var functions = new List<WasmFunctionDefinition>();
        var boundaries = new List<ManagedBoundaryPlanEntry>();
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);

        var result = appender.Append(
            functions,
            4,
            [],
            ImmutableDictionary<int, int>.Empty,
            method,
            TestRuntimeInitialization.Create(256),
            entryPointProfile,
            null,
            new Dictionary<string, int>(),
            new FixedFunctionIndexResolver(),
            boundaries);

        Assert.Equal(3, functions.Count);
        Assert.Equal((4, 5, 6), (
            result.FilterDispatcherIndex,
            result.FinalizerDispatcherIndex,
            result.EntryPointIndex));
        Assert.Equal(hasFinalizers, result.HasFinalizers);
        Assert.Equal(expectedBoundaryCount, boundaries.Count);
        Assert.Equal(hasFinalizers, entries.HasFinalizers);
        Assert.Equal(entryPointProfile == WasmEntryPointProfile.Process,
            entries.ReportTerminalExceptions);
        Assert.Equal(hasFinalizers ? [1, 3] : Array.Empty<int>(),
            finalizers.TypeIds);
    }

    [Fact]
    public void RejectsMissingInputsAndNegativeValues()
    {
        var appender = CreateAppender();
        var functions = new List<WasmFunctionDefinition>();
        var filters = ImmutableArray<FilterFunclet>.Empty;
        var filterIndices = ImmutableDictionary<int, int>.Empty;
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var functionIndices = new FixedFunctionIndexResolver();
        var helpers = new Dictionary<string, int>();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        Assert.Throws<ArgumentNullException>(() => appender.Append(
            null!, 0, filters, filterIndices, method, TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process, null, helpers, functionIndices, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, -1, filters, filterIndices, method, TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process, null, helpers, functionIndices, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, filters, null!, method, TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process, null, helpers, functionIndices, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, filters, filterIndices, null!, TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process, null, helpers, functionIndices, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, 0, filters, filterIndices, method, TestRuntimeInitialization.Create(-1),
            WasmEntryPointProfile.Process, null, helpers, functionIndices, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, filters, filterIndices, method, TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process, null, null!, functionIndices, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, filters, filterIndices, method, TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process, null, helpers, null!, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, filters, filterIndices, method, TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process, null, helpers, functionIndices, null!));
    }

    [Fact]
    public void ProcessEntryUsesTheSharedAsynchronousBoundary()
    {
        var entries = new RecordingEntryPointEmitter();
        var wrapper = new FixedAsyncWrapperEmitter();
        var helpers = new FixedAsyncHelperAppender();
        var functionTypes = new FixedFunctionTypeResolver();
        var boundaryBuilder = new FixedBoundaryBuilder();
        IRuntimeFunctionAppender appender = new[]
        {
            new RuntimeFunctionAppender(
                new FixedDescriptorSource(false),
                new FixedFilterDispatcherEmitter(),
                new RecordingFinalizerEmitter(),
                entries,
                functionTypes,
                new OutwardMethodFunctionAppender(
                    wrapper,
                    helpers,
                    entries,
                    functionTypes,
                    boundaryBuilder),
                boundaryBuilder),
        }.Cast<IRuntimeFunctionAppender>().Single();
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "TaskSource",
                isValueType: false),
            [],
            method.Signature);
        var binding = new JavaScriptAsyncMethodBinding(
            method.Key,
            new(JavaScriptAsyncReturnKind.Task, null),
            instance.DeclaringType,
            instance,
            instance,
            instance);
        var helperIndices = new Dictionary<string, int>();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        var result = appender.Append(
            [],
            0,
            [],
            ImmutableDictionary<int, int>.Empty,
            method,
            TestRuntimeInitialization.Create(0),
            WasmEntryPointProfile.Process,
            binding,
            helperIndices,
            new FixedFunctionIndexResolver(),
            boundaries);

        Assert.Equal(2, result.EntryPointIndex);
        Assert.Equal(1, wrapper.Count);
        Assert.Equal(1, helpers.Count);
        Assert.Equal(0, entries.Count);
        Assert.Equal(
            ManagedBoundaryKind.AsynchronousProcessStart,
            boundaries[^1].Kind);
    }

    private static IRuntimeFunctionAppender CreateAppender() => new[]
    {
        CreateRuntimeAppender(),
    }.Cast<IRuntimeFunctionAppender>().Single();

    private static RuntimeFunctionAppender CreateRuntimeAppender()
    {
        var entries = new RecordingEntryPointEmitter();
        var functionTypes = new FixedFunctionTypeResolver();
        var boundaries = new FixedBoundaryBuilder();
        return new(
            new FixedDescriptorSource(false),
            new FixedFilterDispatcherEmitter(),
            new RecordingFinalizerEmitter(),
            entries,
            functionTypes,
            CreateOutward(entries, functionTypes, boundaries),
            boundaries);
    }

    private static OutwardMethodFunctionAppender CreateOutward(
        IEntryPointEmitter entries,
        IManagedMethodFunctionTypeResolver functionTypes,
        IManagedBoundaryPlanBuilder boundaries) =>
        new OutwardMethodFunctionAppender(
            new FixedAsyncWrapperEmitter(),
            new FixedAsyncHelperAppender(),
            entries,
            functionTypes,
            boundaries);

    private sealed class FixedDescriptorSource(bool withFinalizers) :
        ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => withFinalizers
            ?
            [
                new(EmitterTestSupport.TypeKey, 3, 0, 0, 0, 0,
                    EmitterTestSupport.EntryKey),
                new(EmitterTestSupport.TypeKey, 2, 0, 0, 0, 0, null),
                new(EmitterTestSupport.TypeKey, 1, 0, 0, 0, 0,
                    EmitterTestSupport.ConstructorKey),
            ]
            : [];
        public ImmutableArray<ConstructedTypeDescriptorLayout>
            ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
    }

    private sealed class FixedFilterDispatcherEmitter : IFilterDispatcherEmitter
    {
        public byte[] Emit(
            ImmutableArray<FilterFunclet> filters,
            IReadOnlyDictionary<int, int> functionIndices) => [1];
    }

    private sealed class RecordingFinalizerEmitter : IFinalizerDispatcherEmitter
    {
        public int[] TypeIds { get; private set; } = [];

        public byte[] Emit(
            TypeDescriptorLayout[] finalizableTypes,
            IFunctionIndexResolver functionIndices)
        {
            TypeIds = [.. finalizableTypes.Select(type => type.TypeId)];
            return [2];
        }
    }

    private sealed class RecordingEntryPointEmitter : IEntryPointEmitter
    {
        public int Count { get; private set; }
        public bool HasFinalizers { get; private set; }
        public bool ReportTerminalExceptions { get; private set; }

        public byte[] Emit(
            MethodDefinitionModel entryPoint,
            RuntimeInitializationPlan initialization,
            bool hasFinalizers,
            IFunctionIndexResolver functionIndices,
            bool reportTerminalExceptions,
            EntityKey? argumentFactory)
        {
            Count++;
            HasFinalizers = hasFinalizers;
            ReportTerminalExceptions = reportTerminalExceptions;
            return [3];
        }
    }

    private sealed class FixedAsyncWrapperEmitter : IAsyncJSExportWrapperEmitter
    {
        public int Count { get; private set; }

        public byte[] Emit(
            MethodDefinitionModel method,
            JavaScriptAsyncMethodBinding binding,
            RuntimeInitializationPlan initialization,
            bool hasFinalizers,
            IFunctionIndexResolver functionIndices,
            EntityKey? argumentFactory)
        {
            Count++;
            return [4];
        }
    }

    private sealed class FixedAsyncHelperAppender : IAsyncJSExportHelperAppender
    {
        public int Count { get; private set; }

        public void Append(
            IList<WasmFunctionDefinition> functions,
            int importCount,
            IDictionary<string, int> indices,
            JavaScriptAsyncMethodBinding binding,
            ManagedAsyncBoundaryNames names,
            ManagedAsyncBoundaryKinds kinds,
            ICollection<ManagedBoundaryPlanEntry> boundaryEntries)
        {
            Count++;
        }
    }

    private sealed class FixedFunctionTypeResolver : IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);

        public WasmFunctionType Resolve(MethodInstanceModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public int Resolve(EntityKey method) => 0;
        public int Resolve(string method) => 0;
        public int Resolve(MethodInstanceModel method) => 0;
    }

    private sealed class FixedBoundaryBuilder : IManagedBoundaryPlanBuilder
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
