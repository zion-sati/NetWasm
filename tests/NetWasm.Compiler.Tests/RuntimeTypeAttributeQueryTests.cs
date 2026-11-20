// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Tests;

// The collaborators that answer an attribute query on a Type known only at run time: the Type-value classifier the
// provenance consults, the resolver adapter, the candidate selector, the dispatch builder and the reducer.
public sealed class RuntimeTypeAttributeQueryTests
{
    private static readonly AssemblyIdentity Assembly = new("RuntimeTypeQueries");
    private static readonly CliTypeIdentity TypeType = CliTypeIdentity.Named(Assembly, "System", "Type", false);
    private static readonly CliTypeIdentity DerivedType = CliTypeIdentity.Named(Assembly, "System", "RuntimeType", false);
    private static readonly CliTypeIdentity Marker = CliTypeIdentity.Named(Assembly, "Example", "MarkerAttribute", false);
    private static readonly CliTypeIdentity Holder = CliTypeIdentity.Named(Assembly, "Example", "Holder", false);
    private static readonly CliTypeIdentity Plain = CliTypeIdentity.Named(Assembly, "Example", "Plain", false);
    private static readonly CliTypeIdentity Bool = CliTypeIdentity.Named(Assembly, "System", "Boolean", true);
    private static readonly AttributeQueryCall ManyCall = new(AttributeQueryOperation.GetMany, 3, 1, null, 2);

    [Fact]
    public void TypeValuesAreTypeAndTheClassesDerivedFromIt()
    {
        var relationships = new Relationships(DerivedType);
        var classifier = new AttributeTypeValueClassifier(relationships, TypeType);

        Assert.True(classifier.IsTypeValue(TypeType));
        Assert.True(classifier.IsTypeValue(DerivedType));
        Assert.False(classifier.IsTypeValue(Plain));
        Assert.False(classifier.IsTypeValue(CliTypeIdentity.SzArray(TypeType)));
        Assert.Equal([DerivedType, Plain], relationships.Asked);
        Assert.Throws<ArgumentNullException>(() => classifier.IsTypeValue(null!));
        Assert.Throws<ArgumentNullException>(() => new AttributeTypeValueClassifier(null!, TypeType));
        Assert.Throws<ArgumentNullException>(() => new AttributeTypeValueClassifier(relationships, null!));
    }

    [Fact]
    public void ARunTimeTypeTargetResolvesAsAClosedStandInAndKeepsNoTarget()
    {
        var inner = new StaticResolver { Answer = new(ManyCall, Holder, Marker, true) };
        var resolver = new RuntimeTypeAttributeQueryResolver(inner, Plain);
        ImmutableArray<AttributeValueProof> stack =
            [default, new(AttributeValueKind.RuntimeType), new(AttributeValueKind.Type, Marker), new(AttributeValueKind.Int32, Integer: 1)];

        var query = resolver.ResolveRuntimeType(ManyCall, stack);

        Assert.Equal(new(ManyCall, null, Marker, true), query);
        Assert.Equal(new(AttributeValueKind.Type, Plain), inner.Stack[1]);
        Assert.Equal(stack[2], inner.Stack[2]);
        inner.Answer = null;
        Assert.Null(resolver.ResolveRuntimeType(ManyCall, stack));
        Assert.Equal(2, inner.Calls);
        Assert.Null(resolver.ResolveRuntimeType(ManyCall, stack.SetItem(1, new(AttributeValueKind.Type, Holder))));
        Assert.Equal(2, inner.Calls);
        Assert.Throws<ArgumentNullException>(() => resolver.ResolveRuntimeType(null!, stack));
        Assert.Throws<ArgumentException>(() => resolver.ResolveRuntimeType(ManyCall, default));
        Assert.Throws<ArgumentException>(() => resolver.ResolveRuntimeType(ManyCall, [default]));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAttributeQueryResolver(null!, Plain));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAttributeQueryResolver(inner, null!));
    }

    [Fact]
    public void CandidatesAreTheRunTimeTypesThatCarryTheAttributeInOrderAndArePlannedOnce()
    {
        var generic = CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(Assembly, "Example", "Box`1", false), [Holder]);
        var primitive = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var planner = new Planner(type => type == Plain ? None(AttributeQueryOperation.GetMany) : Found(AttributeQueryOperation.GetMany));
        var selector = new RuntimeTypeAttributeCandidateSelector(planner);
        CliTypeIdentity[] types = [Plain, Holder, generic, primitive, CliTypeIdentity.SzArray(Holder),
            CliTypeIdentity.GenericParameter(method: false, 0), Holder];

        var cases = selector.Select(Query(AttributeQueryOperation.GetMany, null), types);

        Assert.NotNull(cases);
        Assert.Equal([.. new[] { Holder, generic, primitive }.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            cases.Value.Select(found => found.Type));
        Assert.Equal(4, planner.Planned.Count);
        Assert.All(planner.Planned, query => Assert.Equal(Marker, query.Filter));
        Assert.Equal(cases.Value.Select(found => found.Type), selector.Select(Query(AttributeQueryOperation.GetMany, null), types)!.Value.Select(found => found.Type));
        Assert.Equal(4, planner.Planned.Count);
    }

    [Theory]
    [InlineData((int)AttributeQueryOperation.IsDefined, true, false, true, true)]
    [InlineData((int)AttributeQueryOperation.IsDefined, false, true, false, true)]
    [InlineData((int)AttributeQueryOperation.IsDefined, true, true, false, false)]
    [InlineData((int)AttributeQueryOperation.GetOne, null, true, false, true)]
    [InlineData((int)AttributeQueryOperation.GetMany, null, false, true, true)]
    [InlineData((int)AttributeQueryOperation.GetMany, null, false, false, false)]
    public void ACandidateAnswersThroughTheResultsItsInheritanceCanReach(int operationValue, bool? inherit,
        bool direct, bool inherited, bool expected)
    {
        var operation = (AttributeQueryOperation)operationValue;
        var selector = new RuntimeTypeAttributeCandidateSelector(new Planner(_ =>
            new(Holder, Marker, Result(operation, direct), Result(operation, inherited))));

        var cases = selector.Select(Query(operation, inherit), [Holder]);

        Assert.Equal(expected ? 1 : 0, cases!.Value.Length);
    }

    [Fact]
    public void AnUnplannableCandidateKeepsTheRuntimeQueryAndTargetedQueriesAreRejected()
    {
        var selector = new RuntimeTypeAttributeCandidateSelector(new Planner(_ =>
            throw new CompilerException(new(DiagnosticCode.UnsupportedMetadata, "unplannable"))));

        Assert.Null(selector.Select(Query(AttributeQueryOperation.GetMany, true), [Holder]));
        Assert.Throws<ArgumentException>(() => selector.Select(Query(AttributeQueryOperation.GetMany, true) with { Target = Holder }, [Holder]));
        Assert.Throws<ArgumentException>(() => selector.Select(Query(AttributeQueryOperation.GetMany, true) with { TargetAssembly = Assembly }, [Holder]));
        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, [Holder]));
        Assert.Throws<ArgumentNullException>(() => selector.Select(Query(AttributeQueryOperation.GetMany, true), null!));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAttributeCandidateSelector(null!));
    }

    [Fact]
    public void TheDispatchComparesEachCaseAndSplicesItsFragmentOverTheReloadedArguments()
    {
        var fragments = new Fragments();
        var builder = new RuntimeTypeAttributeDispatchBuilder(fragments);
        var nullReference = Method(".ctor", 10, isStatic: false);
        var request = Request([new(Holder, Found(AttributeQueryOperation.GetMany)), new(Plain, Found(AttributeQueryOperation.GetMany))],
            nullReference, nullArgumentName: null);

        var fragment = builder.Build(request);

        Assert.Equal([TypeType, Marker, Bool, Plain, Plain, Plain], fragment.LocalTypes);
        Assert.Equal([7, 8, 9], fragments.Requests.Select(item => item.FirstLocal));
        Assert.All(fragments.Requests, item =>
        {
            Assert.Equal(AttributeQueryOperation.GetMany, item.Operation);
            Assert.Equal(3, item.ArgumentCount);
            Assert.Equal(2, item.InheritArgument);
            Assert.Null(item.InheritConstant);
            Assert.Equal(CliTypeIdentity.SzArray(Marker), item.ResultArrayType);
        });
        Assert.Equal(5, fragment.MaxStack);
        var operations = fragment.Instructions.Select(instruction => instruction.Operation).ToArray();
        Assert.Equal([CilOperation.StoreLocal, CilOperation.StoreLocal, CilOperation.StoreLocal,
            CilOperation.LoadLocal, CilOperation.BranchIfTrue, CilOperation.NewObject, CilOperation.Throw], operations[..7]);
        Assert.Equal(new CilOperand.Index(6), fragment.Instructions[0].Operand);
        Assert.Equal(new CilOperand.Index(4), fragment.Instructions[2].Operand);
        Assert.Equal(new CilOperand.MethodInstance(nullReference), fragment.Instructions[5].Operand);
        Assert.Equal(new CilOperand.BranchTarget(7), fragment.Instructions[4].Operand);
        Assert.Equal(2, fragment.Instructions.Count(instruction =>
            instruction.Operation == CilOperation.MaterializeType && instruction.Operand is CilOperand.TypeIdentity));
        Assert.Equal(CilOperation.Nop, operations[^1]);
        Assert.All(fragment.Instructions.Select((instruction, index) => (instruction, index)),
            item => Assert.Equal(item.index, item.instruction.Offset));
        Assert.All(fragment.Instructions.Where(instruction => instruction.Operand is CilOperand.BranchTarget),
            branch => Assert.InRange(((CilOperand.BranchTarget)branch.Operand).Offset, 0, fragment.Instructions.Length - 1));
        // Each spliced fragment's own branch lands on its own last instruction.
        var spliced = fragment.Instructions.Where(instruction => instruction.Operation == CilOperation.BranchIfFalse &&
            instruction.Operand is CilOperand.BranchTarget { Offset: var target } &&
            fragment.Instructions[target].Operation == CilOperation.Return).ToArray();
        Assert.Equal(3, spliced.Length);
    }

    [Fact]
    public void ADispatchWithoutCasesAnswersOtherwiseAndAStaticQueryNamesItsElement()
    {
        var fragments = new Fragments();
        var argumentNull = Method(".ctor", 11, isStatic: false);
        var fragment = new RuntimeTypeAttributeDispatchBuilder(fragments).Build(Request([], argumentNull, "element"));

        Assert.Single(fragments.Requests);
        Assert.Contains(fragment.Instructions, instruction => instruction is
        { Operation: CilOperation.LoadString, Operand: CilOperand.UserString { Value: "element" } });
        Assert.DoesNotContain(fragment.Instructions, instruction => instruction.Operation == CilOperation.MaterializeType);
        Assert.Throws<ArgumentException>(() => new RuntimeTypeAttributeDispatchBuilder(fragments)
            .Build(Request([], argumentNull, null) with { ArgumentTypes = [TypeType] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeTypeAttributeDispatchBuilder(fragments)
            .Build(Request([], argumentNull, null) with { FirstLocal = -1 }));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAttributeDispatchBuilder(fragments).Build(null!));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeAttributeDispatchBuilder(null!));
    }

    [Fact]
    public void TheReducerLowersEachQueryOnceFromTheMethodsOwnBody()
    {
        var fixture = new ReducerFixture();

        var reduced = fixture.Reduce();

        Assert.Equal([fixture.Name], reduced.Keys);
        Assert.Same(fixture.Output, reduced[fixture.Name]);
        Assert.True(fixture.RuntimeTypes!.SetEquals([Holder, Plain]));
        Assert.Equal([2, 4], fixture.Requests.Select(request => request.FirstLocal));
        Assert.Equal([ReducerFixture.Argument, null], fixture.Requests.Select(request => request.NullArgumentName));
        Assert.Equal([fixture.ArgumentNull, fixture.NullReference], fixture.Requests.Select(request => request.NullTargetException));
        Assert.Equal([TypeType], fixture.Requests[0].ArgumentTypes);
        Assert.Equal([Holder, TypeType], fixture.Requests[1].ArgumentTypes);
        Assert.All(fixture.Requests, request =>
        {
            Assert.Equal(CliTypeIdentity.SzArray(Marker), request.ResultArrayType);
            Assert.Same(fixture.Ambiguity, request.AmbiguityConstructor);
            Assert.Empty(Assert.IsType<AttributeRetrievalResult>(request.Otherwise.Direct).Attributes);
        });
        Assert.Equal([1, 3], fixture.Replacements!.Keys.Order());
    }

    [Fact]
    public void LaterRoundsLowerAgainFromTheOwnBodyOnlyWhenTheCasesChange()
    {
        var fixture = new ReducerFixture();
        fixture.Reduce();
        fixture.Current = fixture.Output;

        Assert.Empty(fixture.Reduce());
        fixture.Cases = [new(Plain, Found(AttributeQueryOperation.IsDefined))];
        var reduced = fixture.Reduce();

        Assert.Same(fixture.Output, reduced[fixture.Name]);
        Assert.All(fixture.Lowered, body => Assert.Same(fixture.Body, body));
        Assert.Equal(3, fixture.Lowered.Count);
    }

    [Theory]
    [InlineData("queries")]
    [InlineData("proofs")]
    [InlineData("resolve")]
    [InlineData("select")]
    public void BodiesWithoutAnswerableQueriesAreLeftAsTheyAre(string missing)
    {
        var fixture = new ReducerFixture { Missing = missing };

        Assert.Empty(fixture.Reduce());
        Assert.Equal(missing == "queries" ? 0 : 1, fixture.Lowered.Count);
        Assert.Throws<ArgumentNullException>(() => fixture.Create().Reduce(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => fixture.Create().Reduce(fixture.Program(), null!));
    }

    [Fact]
    public void AProgramWithoutAttributesHasNothingToReduce()
    {
        var program = new ReducerFixture().Program();
        var reducer = EmptyProgramReducer.Instance;

        Assert.Empty(reducer.Reduce(program, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(program, null!));
    }

    private static ResolvedAttributeQuery Query(AttributeQueryOperation operation, bool? inherit) =>
        new(new(operation, 2, 1, null, inherit is null ? 0 : null), null, Marker, inherit);

    private static AttributeQueryResult Result(AttributeQueryOperation operation, bool found) =>
        operation == AttributeQueryOperation.IsDefined
            ? new AttributeExistenceResult(found)
            : new AttributeRetrievalResult(found ? [new AttributeConstructionPlan(Marker, Method(".ctor", 5, isStatic: false), [], [])] : []);

    private static AttributeQueryEntry Found(AttributeQueryOperation operation) =>
        new(Holder, Marker, Result(operation, true), Result(operation, true));

    private static AttributeQueryEntry None(AttributeQueryOperation operation) =>
        new(Plain, Marker, Result(operation, false), Result(operation, false));

    private static RuntimeTypeAttributeDispatchRequest Request(ImmutableArray<RuntimeTypeAttributeCase> cases,
        MethodInstanceModel nullTarget, string? nullArgumentName) =>
        new(ManyCall, [TypeType, Marker, Bool], cases, None(AttributeQueryOperation.GetMany), null,
            CliTypeIdentity.SzArray(Marker), null, nullTarget, nullArgumentName, 4);

    private static MethodInstanceModel Method(string name, int token, bool isStatic, params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), [.. parameters]);
        return new(new(new(Assembly, token), new(Assembly, 0x02000001), name, isStatic, signature, 0), Holder, [], signature);
    }

    private sealed class Relationships(params CliTypeIdentity[] compatible) : ITypeRelationshipClassifier
    {
        public List<CliTypeIdentity> Asked { get; } = [];

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            Asked.Add(candidate);
            return new(compatible.Contains(candidate) ? TypeRelationshipCharacteristics.Hierarchy : TypeRelationshipCharacteristics.None);
        }
    }

    private sealed class StaticResolver : IAttributeQueryResolver
    {
        public ResolvedAttributeQuery? Answer { get; set; }
        public ImmutableArray<AttributeValueProof> Stack { get; private set; }
        public int Calls { get; private set; }

        public ResolvedAttributeQuery? Resolve(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack)
        {
            Calls++;
            Stack = stack;
            return Answer;
        }
    }

    private sealed class Planner(Func<CliTypeIdentity, AttributeQueryEntry> plan) : IAttributeQueryPlanner
    {
        public List<ResolvedAttributeQuery> Planned { get; } = [];

        public AttributeQueryEntry Plan(ResolvedAttributeQuery query)
        {
            Planned.Add(query);
            return plan(query.Target!);
        }
    }

    // A fragment that pops the arguments and ends in a branch to its own last instruction.
    private sealed class Fragments : IAttributeQueryCilBuilder
    {
        public List<AttributeQueryCilRequest> Requests { get; } = [];

        public AttributeCilFragment Build(AttributeQueryCilRequest request)
        {
            Requests.Add(request);
            return new([
                new(0, 1, CilOperation.Pop, new CilOperand.None()),
                new(1, 2, CilOperation.Pop, new CilOperand.None()),
                new(2, 3, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4)),
                new(3, 4, CilOperation.LoadNull, new CilOperand.None()),
                new(4, 5, CilOperation.Return, new CilOperand.None())], [Plain], 5);
        }
    }

    private sealed class ReducerFixture : ICalledMethodResolver, IAttributeQueryClassifier, IControlFlowGraphBuilder,
        ITypedStackValidator, IAttributeProvenanceAnalyzer, IRuntimeTypeAttributeQueryResolver,
        IRuntimeTypeAttributeCandidateSelector, IRuntimeTypeAttributeDispatchBuilder, IAttributeQueryBodyRewriter
    {
        private readonly MethodInstanceModel _caller = Method("Caller", 1, isStatic: true);
        private readonly MethodInstanceModel _staticQuery = Method("GetCustomAttributes", 2, isStatic: true, TypeType);
        private readonly MethodInstanceModel _instanceQuery = Method("GetCustomAttributes", 3, isStatic: false, TypeType);
        private readonly MethodInstanceModel _ordinary = Method("Ordinary", 4, isStatic: true);
        private readonly AttributeQueryCall _call = new(AttributeQueryOperation.GetMany, 1, 0, null, null);

        internal ReducerFixture()
        {
            Body = new(_caller.Definition, 2, [CliValueKind.I4, CliValueKind.I4],
                [new(0, 1, CilOperation.Nop, new CilOperand.None()),
                 new(1, 2, CilOperation.Call, new CilOperand.None()),
                 new(2, 3, CilOperation.Call, new CilOperand.None()),
                 new(3, 4, CilOperation.CallVirtual, new CilOperand.None()),
                 new(4, 5, CilOperation.Return, new CilOperand.None())])
            { MethodInstance = _caller };
            Output = Body with { MaxStack = 6 };
            Current = Body;
        }

        internal CilMethodBody Body { get; }
        internal CilMethodBody Output { get; }
        internal CilMethodBody Current { get; set; }
        internal string Name => _caller.CanonicalName;
        internal const string Argument = "element";
        internal string? Missing { get; init; }
        internal MethodInstanceModel Ambiguity { get; } = Method(".ctor", 20, isStatic: false);
        internal MethodInstanceModel NullReference { get; } = Method(".ctor", 21, isStatic: false);
        internal MethodInstanceModel ArgumentNull { get; } = Method(".ctor", 22, isStatic: false);
        internal ImmutableArray<RuntimeTypeAttributeCase> Cases { get; set; } = [new(Holder, Found(AttributeQueryOperation.GetMany))];
        internal ImmutableHashSet<CliTypeIdentity>? RuntimeTypes { get; private set; }
        internal List<CilMethodBody> Lowered { get; } = [];
        internal List<RuntimeTypeAttributeDispatchRequest> Requests { get; } = [];
        internal ImmutableDictionary<int, AttributeCilFragment>? Replacements { get; private set; }
        private RuntimeTypeAttributeQueryReducer? _reducer;

        internal RuntimeTypeAttributeQueryReducer Create() => _reducer ??= new(this, this, this, this, this, this, this, this,
            this, Ambiguity, NullReference, ArgumentNull);

        internal ImmutableDictionary<string, CilMethodBody> Reduce() =>
            ((NetWasm.Compiler.Analysis.Constants.IReachableProgramReducer)Create()).Reduce(Program(), ReachabilityRoots.Empty);

        internal ReachableProgram Program()
        {
            var graph = new ControlFlowGraphBuilderFactory().Create().Build(Current);
            var body = new ManagedMethodBody(_caller, new(graph,
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
            return new ReachableProgram(null, ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(_caller.Definition.Key, body),
                [], [], [], [], [], [], ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
                ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty, [])
            {
                ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(Name, body),
                RuntimeTypes = [Holder],
                AllocatedTypes = [Plain, Holder],
            };
        }

        public MethodInstanceModel? Resolve(CilInstruction instruction) => instruction.Offset switch
        {
            1 => _staticQuery,
            2 => _ordinary,
            3 => _instanceQuery,
            _ => null,
        };

        public AttributeQueryCall? Classify(MethodInstanceModel method) =>
            Missing == "queries" || method == _ordinary ? null : _call;

        public ControlFlowGraph Build(CilMethodBody body)
        {
            Lowered.Add(body);
            return new ControlFlowGraphBuilderFactory().Create().Build(body);
        }

        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) => new(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

        public ImmutableDictionary<int, ImmutableArray<AttributeValueProof>> Analyze(ValidatedControlFlowGraph controlFlow)
        {
            ImmutableArray<AttributeValueProof> proof = [new(AttributeValueKind.RuntimeType)];
            return Missing == "proofs"
                ? ImmutableDictionary<int, ImmutableArray<AttributeValueProof>>.Empty
                : ImmutableDictionary<int, ImmutableArray<AttributeValueProof>>.Empty.Add(1, proof).Add(3, proof);
        }

        public ResolvedAttributeQuery? ResolveRuntimeType(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack) =>
            Missing == "resolve" ? null : new(call, null, Marker, true);

        public ImmutableArray<RuntimeTypeAttributeCase>? Select(ResolvedAttributeQuery query, IEnumerable<CliTypeIdentity> runtimeTypes)
        {
            RuntimeTypes = [.. runtimeTypes];
            return Missing == "select" ? null : Cases;
        }

        public AttributeCilFragment Build(RuntimeTypeAttributeDispatchRequest request)
        {
            Requests.Add(request);
            return new([new(0, 1, CilOperation.Nop, new CilOperand.None())], [Plain, Plain], 4);
        }

        public CilMethodBody Rewrite(CilMethodBody body, ImmutableDictionary<int, AttributeCilFragment> replacements)
        {
            Assert.Same(Body, body);
            Replacements = replacements;
            return Output;
        }
    }
}
