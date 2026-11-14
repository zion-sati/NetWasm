using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class RequiredTypeDependencyProgramBuilderTests
{
    [Fact]
    public void ValidatesOrdinaryConstructedAndMethodArgumentTypesBeforeDelegating()
    {
        var key = new EntityKey(new("Test"), 0x02000001);
        var root = CliTypeIdentity.Named(new("Test"), "Test", "Root", false);
        var constructed = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new("Test"), "Test", "Box`1", false), [root]);
        var argument = CliTypeIdentity.Named(new("Test"), "Test", "Argument", false);
        var method = new MethodInstanceModel(
            new(new(new("Test"), 0x06000001), key, "Identity", true, null!, 0),
            root, [argument], null!);
        var state = ReachabilityLedgerSnapshot.From(new ReachabilityLedger()) with
        {
            Types = [key],
            ConstructedTypes = [constructed],
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(method.CanonicalName, method)
        };
        var inner = new InnerBuilder();
        var validator = new Validator(inner);
        var identities = new Identities(key, root);
        var builder = Assert.IsAssignableFrom<IReachableProgramBuilder>(
            new RequiredTypeDependencyProgramBuilder(inner, validator, identities, new CompilerDiagnosticDependencyPathBinder()));

        var result = builder.Build(null, [], state, null!, null!);

        Assert.Same(inner.Result, result);
        Assert.Same(state, inner.State);
        Assert.Equal([root, constructed, argument], validator.Types);
        Assert.Equal([key], state.Types);
        Assert.Equal([constructed], state.ConstructedTypes);
        Assert.True(inner.Called);
        Assert.Equal(key, Assert.Single(identities.Calls));
    }

    [Fact]
    public void ValidationFailurePreventsProgramConstruction()
    {
        var state = ReachabilityLedgerSnapshot.From(new ReachabilityLedger());
        var inner = new InnerBuilder();
        var expected = new CompilerException(new(DiagnosticCode.AssemblyResolution, "Required dependency"));
        var validator = new Validator(inner) { Error = expected };
        var builder = Assert.IsAssignableFrom<IReachableProgramBuilder>(
            new RequiredTypeDependencyProgramBuilder(inner, validator, new Identities(default, null!), new CompilerDiagnosticDependencyPathBinder()));

        var error = Assert.Throws<CompilerException>(() => builder.Build(null, [], state, null!, null!));

        Assert.Same(expected, error);
        Assert.Empty(validator.Types);
        Assert.False(inner.Called);
    }

    private sealed class Validator(InnerBuilder inner) : IRequiredTypeDependencyValidator
    {
        public CliTypeIdentity[] Types { get; private set; } = [];
        public CompilerException? Error { get; init; }
        public void Validate(IEnumerable<CliTypeIdentity> types)
        {
            Assert.False(inner.Called);
            Types = types.ToArray();
            if (Error is not null)
                throw Error;
        }
    }

    private sealed class Identities(EntityKey key, CliTypeIdentity type) : ITypeIdentityResolver
    {
        public List<EntityKey> Calls { get; } = [];
        public CliTypeIdentity GetTypeIdentity(EntityKey input)
        {
            Assert.Equal(key, input);
            Calls.Add(input);
            return type;
        }
    }

    private sealed class InnerBuilder : IReachableProgramBuilder
    {
        public bool Called { get; private set; }
        public ReachabilityLedgerSnapshot? State { get; private set; }
        public ReachableProgram Result { get; } = new(
            null, ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty, [], [], [], [], [], [],
            ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
            ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty, []);

        public ReachableProgram Build(
            MethodDefinitionModel? entryPoint,
            ImmutableArray<ProgramExport> exports,
            ReachabilityLedgerSnapshot state,
            IDelegateTypeRecognizer delegateTypes,
            ITypeTestPlanner typeTestPlanner)
        {
            Called = true;
            State = state;
            return Result;
        }
    }
}
