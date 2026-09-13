using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedFunctionAppenderTests
{
    [Fact]
    public void AppendsDefinitionFunctionEnvironmentAndMetricRecord()
    {
        var fixture = new Fixture();
        var body = new RecordingBodyEmitter();
        var appender = AsDefinition(new ManagedDefinitionFunctionAppender(
            fixture.Program, fixture.Program, body, new FixedFunctionTypeResolver()));

        appender.Append(fixture.Functions, fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"),
            EmitterTestSupport.EntryKey, fixture.Structured, fixture.RootMap,
            fixture.Target, fixture.FunctionIndices);

        Assert.Single(fixture.Functions);
        Assert.Equal(body.Result.Body, fixture.Functions[0].Body);
        Assert.Same(body.Result.FilterEnvironment, fixture.Environments["method"]);
        Assert.Equal(EmitterTestSupport.EntryKey.ToString(),
            Assert.Single(fixture.Emissions).Identity);
        Assert.Null(body.MethodInstance);
    }

    [Fact]
    public void AppendsConstructedFunctionEnvironmentAndMetricRecord()
    {
        var fixture = new Fixture();
        var body = new RecordingBodyEmitter();
        var method = fixture.CreateMethodInstance();
        var appender = AsConstructed(new ConstructedMethodFunctionAppender(
            body, new FixedFunctionTypeResolver()));

        appender.Append(fixture.Functions, fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"),
            method, fixture.Structured, fixture.RootMap, fixture.Target,
            fixture.FunctionIndices);

        Assert.Equal(method.CanonicalName, Assert.Single(fixture.Functions).Name);
        Assert.Same(method, body.MethodInstance);
        Assert.Equal(method.CanonicalName, Assert.Single(fixture.Emissions).Identity);
        Assert.Same(body.Result.FilterEnvironment, fixture.Environments["method"]);
    }

    [Fact]
    public void DefinitionAppenderRejectsMissingCollaborativeInputs()
    {
        var fixture = new Fixture();
        var appender = AsDefinition(new ManagedDefinitionFunctionAppender(
            fixture.Program, fixture.Program, new RecordingBodyEmitter(),
            new FixedFunctionTypeResolver()));

        Assert.Throws<ArgumentNullException>(() => appender.Append(null!, fixture.Environments,
            fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), EmitterTestSupport.EntryKey, fixture.Structured,
            fixture.RootMap, fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions, null!,
            fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), EmitterTestSupport.EntryKey, fixture.Structured,
            fixture.RootMap, fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, null!,
            new ManagedMethodIdentity("test-caller"), EmitterTestSupport.EntryKey, fixture.Structured,
            fixture.RootMap, fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), EmitterTestSupport.EntryKey, null!,
            fixture.RootMap, fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), EmitterTestSupport.EntryKey,
            fixture.Structured, null!, fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), EmitterTestSupport.EntryKey,
            fixture.Structured, fixture.RootMap, null!, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), EmitterTestSupport.EntryKey,
            fixture.Structured, fixture.RootMap, fixture.Target, null!));
    }

    [Fact]
    public void ConstructedAppenderRejectsMissingCollaborativeInputs()
    {
        var fixture = new Fixture();
        var appender = AsConstructed(new ConstructedMethodFunctionAppender(
            new RecordingBodyEmitter(), new FixedFunctionTypeResolver()));
        var method = fixture.CreateMethodInstance();

        Assert.Throws<ArgumentNullException>(() => appender.Append(null!, fixture.Environments,
            fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), method, fixture.Structured, fixture.RootMap,
            fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions, null!,
            fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), method, fixture.Structured, fixture.RootMap,
            fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, null!,
            new ManagedMethodIdentity("test-caller"), method, fixture.Structured, fixture.RootMap,
            fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), null!, fixture.Structured,
            fixture.RootMap, fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), method, null!, fixture.RootMap,
            fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), method, fixture.Structured, null!,
            fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), method, fixture.Structured,
            fixture.RootMap, null!, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions,
            new ManagedMethodIdentity("test-caller"), method, fixture.Structured,
            fixture.RootMap, fixture.Target, null!));
    }

    private static IManagedDefinitionFunctionAppender AsDefinition(
        ManagedDefinitionFunctionAppender appender) => new[] { appender }
        .Cast<IManagedDefinitionFunctionAppender>().Single();

    private static IConstructedMethodFunctionAppender AsConstructed(
        ConstructedMethodFunctionAppender appender) => new[] { appender }
        .Cast<IConstructedMethodFunctionAppender>().Single();

    private sealed class Fixture
    {
        public FakeProgram Program { get; } = new();
        public List<WasmFunctionDefinition> Functions { get; } = [];
        public Dictionary<string, FilterEnvironmentLayout> Environments { get; } = [];
        public List<ManagedMethodEmissionRecord> Emissions { get; } = [];
        public StructuredMethod Structured { get; }
        public MethodRootMap RootMap { get; }
        public InstructionModuleTarget Target { get; }
        public IFunctionIndexResolver FunctionIndices { get; }

        public Fixture()
        {
            var request = EmitterTestSupport.CreateEmissionRequest(Program);
            Structured = request.Methods[EmitterTestSupport.EntryKey];
            RootMap = request.RootMaps[EmitterTestSupport.EntryKey];
            Target = EmitterTestSupport.CreateInstructionModuleTarget(Program);
            FunctionIndices = EmitterTestSupport.CreateFunctionIndexResolver(Program);
        }

        public MethodInstanceModel CreateMethodInstance()
        {
            var definition = Program.GetMethod(EmitterTestSupport.EntryKey);
            return new(definition,
                CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "Generic",
                    isValueType: false),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
                definition.Signature);
        }
    }

    private sealed class RecordingBodyEmitter : IManagedMethodBodyEmitter
    {
        public ManagedMethodBodyEmission Result { get; } = new(
            [1, 2], 3, 4, 5, "method", FilterEnvironmentLayout.Empty,
            ImmutableDictionary<int, int>.Empty);
        public MethodInstanceModel? MethodInstance { get; private set; }

        public ManagedMethodBodyEmission Emit(MethodDefinitionModel method, ManagedMethodIdentity callerIdentity,
StructuredMethod structured, MethodRootMap rootMap,
            InstructionModuleTarget target, IFunctionIndexResolver functionIndices,
            MethodInstanceModel? methodInstance = null)
        {
            MethodInstance = methodInstance;
            return Result;
        }
    }

    private sealed class FixedFunctionTypeResolver : IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);

        public WasmFunctionType Resolve(MethodInstanceModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);
    }
}
