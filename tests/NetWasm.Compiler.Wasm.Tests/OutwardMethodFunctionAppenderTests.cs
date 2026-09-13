using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class OutwardMethodFunctionAppenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendRoutesOneOutwardBoundaryFromItsCompletionShape(
        bool asynchronous)
    {
        var wrappers = new RecordingAsyncWrapperEmitter();
        var helpers = new RecordingAsyncHelperAppender();
        var entries = new RecordingEntryPointEmitter();
        var boundaries = new RecordingBoundaryBuilder();
        IOutwardMethodFunctionAppender appender = new[]
        {
            new OutwardMethodFunctionAppender(
                wrappers,
                helpers,
                entries,
                new FixedFunctionTypeResolver(),
                boundaries),
        }.Cast<IOutwardMethodFunctionAppender>().Single();
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var binding = asynchronous ? CreateBinding(method) : null;
        var names = binding is null
            ? null
            : ManagedAsyncBoundaryNames.ForProcess(binding);
        var kinds = binding is null ? null : ManagedAsyncBoundaryKinds.Process;
        var functions = new List<WasmFunctionDefinition>();
        var helperIndices = new Dictionary<string, int>();
        var boundaryEntries = new List<ManagedBoundaryPlanEntry>();

        var index = appender.Append(new(
            functions,
            3,
            helperIndices,
            "netwasm.entry",
            "run",
            method,
            binding,
            names,
            kinds,
            TestRuntimeInitialization.Create(64),
            true,
            true,
            ManagedBoundaryKind.ProcessEntryPoint,
            new FixedFunctionIndexResolver(),
            boundaryEntries));

        Assert.Equal(3, index);
        Assert.Equal("netwasm.entry", Assert.Single(functions).Name);
        Assert.Equal(asynchronous ? 1 : 0, wrappers.Count);
        Assert.Equal(asynchronous ? 1 : 0, helpers.Count);
        Assert.Equal(asynchronous ? 0 : 1, entries.Count);
        Assert.Equal(
            asynchronous
                ? ManagedBoundaryKind.AsynchronousProcessStart
                : ManagedBoundaryKind.ProcessEntryPoint,
            Assert.Single(boundaryEntries).Kind);
        if (asynchronous)
        {
            Assert.Same(names, helpers.Names);
            Assert.Same(kinds, helpers.Kinds);
            Assert.Equal(CliValueKind.I4, functions[0].Type.Result);
        }
        else
        {
            Assert.True(entries.ReportTerminalExceptions);
        }
    }

    [Fact]
    public void AppendRejectsIncompleteRequests()
    {
        var appender = CreateAppender();
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var binding = CreateBinding(method);
        var valid = CreateRequest(method);

        Assert.Throws<ArgumentNullException>(() => appender.Append(null!));
        Assert.Throws<ArgumentNullException>(() => appender.Append(valid with
        {
            Functions = null!,
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(valid with
        {
            ImportCount = -1,
        }));
        Assert.Throws<ArgumentNullException>(() => appender.Append(valid with
        {
            AsyncHelperIndices = null!,
        }));
        Assert.Throws<ArgumentException>(() => appender.Append(valid with
        {
            GeneratedFunctionName = " ",
        }));
        Assert.Throws<ArgumentException>(() => appender.Append(valid with
        {
            ExportName = "",
        }));
        Assert.Throws<ArgumentNullException>(() => appender.Append(valid with
        {
            Method = null!,
        }));
        Assert.Throws<ArgumentNullException>(() => appender.Append(valid with
        {
            Initialization = null!,
        }));
        Assert.Throws<ArgumentNullException>(() => appender.Append(valid with
        {
            FunctionIndices = null!,
        }));
        Assert.Throws<ArgumentNullException>(() => appender.Append(valid with
        {
            BoundaryEntries = null!,
        }));
        Assert.Throws<ArgumentException>(() => appender.Append(valid with
        {
            AsyncBinding = binding,
            AsyncNames = null,
            AsyncKinds = ManagedAsyncBoundaryKinds.Process,
        }));
        Assert.Throws<ArgumentException>(() => appender.Append(valid with
        {
            AsyncBinding = binding,
            AsyncNames = ManagedAsyncBoundaryNames.ForProcess(binding),
            AsyncKinds = null,
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessArgumentFactoryRemovesTheHostFacingManagedParameter(
        bool asynchronous)
    {
        var wrappers = new RecordingAsyncWrapperEmitter();
        var entries = new RecordingEntryPointEmitter();
        var appender = new OutwardMethodFunctionAppender(
            wrappers,
            new RecordingAsyncHelperAppender(),
            entries,
            new ParameterizedFunctionTypeResolver(),
            new RecordingBoundaryBuilder());
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var binding = asynchronous ? CreateBinding(method) : null;
        var request = CreateRequest(method) with
        {
            AsyncBinding = binding,
            AsyncNames = binding is null
                ? null
                : ManagedAsyncBoundaryNames.ForProcess(binding),
            AsyncKinds = binding is null ? null : ManagedAsyncBoundaryKinds.Process,
            ArgumentFactory = EmitterTestSupport.StringMethodKey,
        };

        appender.Append(request);

        Assert.Empty(Assert.Single(request.Functions).Type.Parameters);
        Assert.Equal(
            EmitterTestSupport.StringMethodKey,
            asynchronous ? wrappers.ArgumentFactory : entries.ArgumentFactory);
    }

    private static OutwardMethodFunctionAppender CreateAppender() => new(
        new RecordingAsyncWrapperEmitter(),
        new RecordingAsyncHelperAppender(),
        new RecordingEntryPointEmitter(),
        new FixedFunctionTypeResolver(),
        new RecordingBoundaryBuilder());

    private static OutwardMethodFunctionAppendRequest CreateRequest(
        MethodDefinitionModel method) => new(
        [],
        0,
        new Dictionary<string, int>(),
        "generated",
        "export",
        method,
        null,
        null,
        null,
        TestRuntimeInitialization.Create(0),
        false,
        false,
        ManagedBoundaryKind.SynchronousExport,
        new FixedFunctionIndexResolver(),
        []);

    private static JavaScriptAsyncMethodBinding CreateBinding(
        MethodDefinitionModel method)
    {
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
            method.Key,
            new(
                JavaScriptAsyncReturnKind.Task,
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            instance.DeclaringType,
            instance,
            instance,
            instance);
    }

    private sealed class RecordingAsyncWrapperEmitter :
        IAsyncJSExportWrapperEmitter
    {
        public int Count { get; private set; }
        public EntityKey? ArgumentFactory { get; private set; }

        public byte[] Emit(
            MethodDefinitionModel method,
            JavaScriptAsyncMethodBinding binding,
            RuntimeInitializationPlan initialization,
            bool hasFinalizers,
            IFunctionIndexResolver functionIndices,
            EntityKey? argumentFactory)
        {
            Count++;
            ArgumentFactory = argumentFactory;
            return [1];
        }
    }

    private sealed class RecordingAsyncHelperAppender :
        IAsyncJSExportHelperAppender
    {
        public int Count { get; private set; }
        public ManagedAsyncBoundaryNames? Names { get; private set; }
        public ManagedAsyncBoundaryKinds? Kinds { get; private set; }

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
            Names = names;
            Kinds = kinds;
        }
    }

    private sealed class RecordingEntryPointEmitter : IEntryPointEmitter
    {
        public int Count { get; private set; }
        public bool ReportTerminalExceptions { get; private set; }
        public EntityKey? ArgumentFactory { get; private set; }

        public byte[] Emit(
            MethodDefinitionModel entryPoint,
            RuntimeInitializationPlan initialization,
            bool hasFinalizers,
            IFunctionIndexResolver functionIndices,
            bool reportTerminalExceptions,
            EntityKey? argumentFactory)
        {
            Count++;
            ReportTerminalExceptions = reportTerminalExceptions;
            ArgumentFactory = argumentFactory;
            return [2];
        }
    }

    private sealed class FixedFunctionTypeResolver :
        IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);

        public WasmFunctionType Resolve(MethodInstanceModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);
    }

    private sealed class ParameterizedFunctionTypeResolver :
        IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) =>
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.ManagedReference);

        public WasmFunctionType Resolve(MethodInstanceModel method) =>
            Resolve(method.Definition);
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public int Resolve(EntityKey method) => 0;
        public int Resolve(string method) => 0;
        public int Resolve(MethodInstanceModel method) => 0;
    }

    private sealed class RecordingBoundaryBuilder : IManagedBoundaryPlanBuilder
    {
        public ManagedBoundaryPlanEntry Build(
            ManagedBoundaryPlanBuildRequest request) => new(
            request.FunctionIndex,
            request.Functions[
                request.FunctionIndex - request.ImportedFunctionCount].Name,
            request.ExportName,
            request.Kind,
            request.IsOutwardFacing,
            ManagedBoundaryFailureDisposition.PropagateManagedException);
    }
}
