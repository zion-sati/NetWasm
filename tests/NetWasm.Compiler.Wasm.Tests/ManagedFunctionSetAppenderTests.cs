using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedFunctionSetAppenderTests
{
    [Fact]
    public void AppendsDefinitionsInPlannedOrder()
    {
        var fixture = new Fixture();
        var leaf = new RecordingDefinitionAppender();
        var appender = AsSet(new ManagedDefinitionSetAppender(leaf));
        var first = EmitterTestSupport.Key(0x06000020);
        var second = EmitterTestSupport.Key(0x06000010);
        var methods = ImmutableDictionary<EntityKey, StructuredMethod>.Empty
            .Add(first, fixture.Structured).Add(second, fixture.Structured);
        var roots = ImmutableDictionary<EntityKey, MethodRootMap>.Empty
            .Add(first, fixture.RootMap).Add(second, fixture.RootMap);

        appender.Append(fixture.Functions, fixture.Environments, fixture.Emissions, [new ManagedDefinitionEmission(new ManagedMethodIdentity("first"), first), new ManagedDefinitionEmission(new ManagedMethodIdentity("second"), second)], methods, roots, fixture.Target, fixture.Resolver);

        Assert.Equal([first, second], leaf.Keys);
    }

    [Fact]
    public void AppendsConstructedMethodsInPlannedOrder()
    {
        var fixture = new Fixture();
        var leaf = new RecordingConstructedAppender();
        var appender = AsSet(new ConstructedMethodSetAppender(leaf));
        var first = fixture.Instance(CliValueKind.I4);
        var second = fixture.Instance(CliValueKind.I8);
        var instances = new Dictionary<string, MethodInstanceModel>
        {
            ["first"] = first,
            ["second"] = second,
        };
        var methods = new Dictionary<string, StructuredMethod>
        {
            ["first"] = fixture.Structured,
            ["second"] = fixture.Structured,
        };
        var roots = new Dictionary<string, MethodRootMap>
        {
            ["first"] = fixture.RootMap,
            ["second"] = fixture.RootMap,
        };

        appender.Append(fixture.Functions, fixture.Environments, fixture.Emissions, [new ManagedMethodIdentity("second"), new ManagedMethodIdentity("first")], instances, methods, roots, fixture.Target,
            fixture.Resolver);

        Assert.Equal([second, first], leaf.Methods);
    }

    [Fact]
    public void DefinitionSetRejectsMissingCollaborativeInputs()
    {
        var fixture = new Fixture();
        var appender = AsSet(new ManagedDefinitionSetAppender(
            new RecordingDefinitionAppender()));
        var methods = new Dictionary<EntityKey, StructuredMethod>();
        var roots = new Dictionary<EntityKey, MethodRootMap>();

        Assert.Throws<ArgumentNullException>(() => appender.Append(null!,
            fixture.Environments, fixture.Emissions, [], methods, roots,
            fixture.Target, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            null!, fixture.Emissions, [], methods, roots, fixture.Target,
            fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, null!, [], methods, roots, fixture.Target,
            fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], null!, roots,
            fixture.Target, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], methods, null!,
            fixture.Target, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], methods, roots, null!,
            fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], methods, roots,
            fixture.Target, null!));
    }

    [Fact]
    public void ConstructedSetRejectsMissingCollaborativeInputs()
    {
        var fixture = new Fixture();
        var appender = AsSet(new ConstructedMethodSetAppender(
            new RecordingConstructedAppender()));
        var instances = new Dictionary<string, MethodInstanceModel>();
        var methods = new Dictionary<string, StructuredMethod>();
        var roots = new Dictionary<string, MethodRootMap>();

        Assert.Throws<ArgumentNullException>(() => appender.Append(null!,
            fixture.Environments, fixture.Emissions, [], instances, methods, roots,
            fixture.Target, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            null!, fixture.Emissions, [], instances, methods, roots, fixture.Target,
            fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, null!, [], instances, methods, roots, fixture.Target,
            fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], null!, methods, roots,
            fixture.Target, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], instances, null!, roots,
            fixture.Target, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], instances, methods, null!,
            fixture.Target, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], instances, methods, roots,
            null!, fixture.Resolver));
        Assert.Throws<ArgumentNullException>(() => appender.Append(fixture.Functions,
            fixture.Environments, fixture.Emissions, [], instances, methods, roots,
            fixture.Target, null!));
    }

    private static IManagedDefinitionSetAppender AsSet(
        ManagedDefinitionSetAppender appender) => new[] { appender }
        .Cast<IManagedDefinitionSetAppender>().Single();

    private static IConstructedMethodSetAppender AsSet(
        ConstructedMethodSetAppender appender) => new[] { appender }
        .Cast<IConstructedMethodSetAppender>().Single();

    private sealed class Fixture
    {
        private readonly FakeProgram _program = new();
        public List<WasmFunctionDefinition> Functions { get; } = [];
        public Dictionary<string, FilterEnvironmentLayout> Environments { get; } = [];
        public List<ManagedMethodEmissionRecord> Emissions { get; } = [];
        public StructuredMethod Structured { get; }
        public MethodRootMap RootMap { get; }
        public InstructionModuleTarget Target { get; }
        public IFunctionIndexResolver Resolver { get; }

        public Fixture()
        {
            var request = EmitterTestSupport.CreateEmissionRequest(_program);
            Structured = request.Methods[EmitterTestSupport.EntryKey];
            RootMap = request.RootMaps[EmitterTestSupport.EntryKey];
            Target = EmitterTestSupport.CreateInstructionModuleTarget(_program);
            Resolver = EmitterTestSupport.CreateFunctionIndexResolver(_program);
        }

        public MethodInstanceModel Instance(CliValueKind argument) => new(
            _program.GetMethod(EmitterTestSupport.EntryKey),
            CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "Generic",
                isValueType: false), [CliTypeIdentity.FromStackKind(argument)],
            _program.GetMethod(EmitterTestSupport.EntryKey).Signature);
    }

    private sealed class RecordingDefinitionAppender :
        IManagedDefinitionFunctionAppender
    {
        public List<EntityKey> Keys { get; } = [];

        public void Append(IList<WasmFunctionDefinition> functions,
            IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
            ICollection<ManagedMethodEmissionRecord> emissions,
            ManagedMethodIdentity callerIdentity, EntityKey methodKey,
            StructuredMethod structured, MethodRootMap rootMap,
            InstructionModuleTarget target, IFunctionIndexResolver functionIndices) =>
            Keys.Add(methodKey);
    }

    private sealed class RecordingConstructedAppender :
        IConstructedMethodFunctionAppender
    {
        public List<MethodInstanceModel> Methods { get; } = [];

        public void Append(IList<WasmFunctionDefinition> functions,
            IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
            ICollection<ManagedMethodEmissionRecord> emissions,
            ManagedMethodIdentity callerIdentity, MethodInstanceModel method, StructuredMethod structured,
            MethodRootMap rootMap, InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices) => Methods.Add(method);
    }
}
