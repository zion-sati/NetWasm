using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Devirtualization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.Devirtualization;

public sealed class ExactReceiverDevirtualizerTests
{
    private static readonly AssemblyIdentity Assembly = new("DevirtualizationFixture");
    private static readonly CliTypeIdentity Base = CliTypeIdentity.Named(Assembly, "", "Base", false);
    private static readonly CliTypeIdentity Derived = CliTypeIdentity.Named(Assembly, "", "Derived", false);
    private static readonly CliTypeIdentity Value = CliTypeIdentity.Named(Assembly, "", "Value", true);
    private static readonly MethodInstanceModel Virtual = Method(1, Base, "M", isVirtual: true);
    private static readonly MethodInstanceModel Implementation = Method(2, Derived, "M", isVirtual: true);
    private static readonly MethodInstanceModel WithArgument = Method(3, Base, "N", isVirtual: true, parameters: [CliValueKind.I4]);
    private static readonly MethodInstanceModel Constructor = Method(4, Derived, ".ctor");
    private static readonly MethodInstanceModel ValueConstructor = Method(5, Value, ".ctor");
    private static readonly MethodInstanceModel Abstract = Method(6, Derived, "M", isVirtual: true, body: false);
    private static readonly CliTypeIdentity Integers = CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(CliValueKind.I4));
    private static readonly MethodInstanceModel ArrayHelper = Method(7, Base, "GetEnumerator", isStatic: true,
        parameters: [CliValueKind.ManagedReference]);

    [Fact]
    public void CallsTheAllocatedClassImplementationDirectly()
    {
        var fixture = new Fixture(Allocated());

        var result = fixture.Rewrite();

        Assert.Equal(CilOperation.Call, result.Instructions[3].Operation);
        Assert.Equal(new CilOperand.MethodInstance(Implementation), result.Instructions[3].Operand);
        foreach (var index in Enumerable.Range(0, 6).Where(static index => index != 3))
        {
            Assert.Same(fixture.Body.Instructions[index], result.Instructions[index]);
        }
        Assert.Equal(0, fixture.Validations);
    }

    [Fact]
    public void FollowsADuplicatedAllocationAndFindsTheReceiverBelowArguments()
    {
        var fixture = new Fixture(
        [
            new(0, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
            new(1, CilOperation.Duplicate, new CilOperand.None(), [CliValueKind.ManagedReference]),
            new(2, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference, CliValueKind.ManagedReference]),
            new(3, CilOperation.Pop, new CilOperand.None(), [CliValueKind.ManagedReference]),
            new(4, CilOperation.LoadLocal, new CilOperand.Index(0), []),
            new(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(7), [CliValueKind.ManagedReference]),
            new(6, CilOperation.CallVirtual, new CilOperand.MethodInstance(WithArgument), [CliValueKind.ManagedReference, CliValueKind.I4]),
            new(7, CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]),
        ])
        { Target = Implementation };

        Assert.Equal(CilOperation.Call, fixture.Rewrite().Instructions[6].Operation);
    }

    [Fact]
    public void ComputesStackFactsOnlyWhenNoneAreKnown()
    {
        var fixture = new Fixture(Allocated()) { KnownStacks = false };

        Assert.Equal(CilOperation.Call, fixture.Rewrite().Instructions[3].Operation);
        Assert.Equal(1, fixture.Validations);
    }

    [Theory]
    // Disabled, no allocation, no virtual call, two stores, an exposed local,
    // a constrained call, a value-type allocation, an unresolved target and an
    // abstract one, a receiver that is not a local, a static callee and a
    // receiver whose stack facts are missing.
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void LeavesUnprovenCallsVirtual(int shape)
    {
        var instructions = Allocated();
        var fixture = shape switch
        {
            0 => new Fixture(instructions) { Enabled = false },
            1 => new Fixture(instructions.SetItem(0, instructions[0] with { Operation = CilOperation.LoadNull, Operand = new CilOperand.None() })),
            2 => new Fixture(instructions.SetItem(3, instructions[3] with { Operation = CilOperation.Call })),
            3 => new Fixture([.. instructions.Take(4),
                new(4, CilOperation.LoadNull, new CilOperand.None(), [CliValueKind.I4]),
                new(5, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.I4, CliValueKind.ManagedReference]),
                new(6, CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
                new(7, CilOperation.Return, new CilOperand.None(), [])]),
            4 => new Fixture([.. instructions.Take(5),
                new(5, CilOperation.LoadLocalAddress, new CilOperand.Index(0), []),
                new(6, CilOperation.Pop, new CilOperand.None(), [CliValueKind.ManagedAddress]),
                new(7, CilOperation.Return, new CilOperand.None(), [])]),
            5 => new Fixture(instructions.SetItem(2, new(2, CilOperation.Constrained, new CilOperand.TypeIdentity(Derived), [])).Insert(2,
                new(2, CilOperation.LoadLocal, new CilOperand.Index(0), [])) is var constrained
                ? [.. constrained.Select((instruction, index) => instruction with { Offset = index })] : []),
            6 => new Fixture(instructions.SetItem(0, instructions[0] with { Operand = new CilOperand.MethodInstance(ValueConstructor) })),
            7 => new Fixture(instructions) { Target = null },
            8 => new Fixture(instructions) { Target = Abstract },
            9 => new Fixture(instructions.SetItem(2, instructions[2] with { Operation = CilOperation.LoadArgument })),
            10 => new Fixture(instructions.SetItem(3, instructions[3] with { Operand = new CilOperand.MethodInstance(Method(7, Base, "S", isStatic: true)) })),
            11 => new Fixture(instructions) { MissingStackAt = 3 },
            _ => new Fixture(instructions.SetItem(3, instructions[3] with { Operand = new CilOperand.None() })),
        };

        Assert.Same(fixture.Body, fixture.Rewrite());
    }

    // Roslyn reuses one local for the enumerators of successive foreach loops:
    // each store is an allocation of the same class, and each call follows one.
    [Fact]
    public void ALocalReusedForAllocationsOfOneClassStaysExact()
    {
        var fixture = new Fixture(
        [
            new(0, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
            new(1, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
            new(2, CilOperation.LoadLocal, new CilOperand.Index(0), []),
            new(3, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
            new(4, CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
            new(5, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
            new(6, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
            new(7, CilOperation.LoadLocal, new CilOperand.Index(0), []),
            new(8, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
            new(9, CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
            new(10, CilOperation.Return, new CilOperand.None(), []),
        ]);

        var rewritten = fixture.Rewrite();

        Assert.Equal(CilOperation.Call, rewritten.Instructions[3].Operation);
        Assert.Equal(CilOperation.Call, rewritten.Instructions[8].Operation);
    }

    [Theory]
    // The stored value has no stack facts, comes from either of two
    // allocations, or is an array allocated through its constructor.
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheStoredValueMustBeOneKnownAllocationOfAClass(int shape)
    {
        var array = CliTypeIdentity.Array(Base, 2);
        var fixture = shape switch
        {
            0 => new Fixture(Allocated()) { MissingStackAt = 1 },
            1 => new Fixture(
            [
                new(0, CilOperation.LoadArgument, new CilOperand.Index(0), []),
                new(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4), [CliValueKind.I4]),
                new(2, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
                new(3, CilOperation.Branch, new CilOperand.BranchTarget(5), [CliValueKind.ManagedReference]),
                new(4, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
                new(5, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
                new(6, CilOperation.LoadLocal, new CilOperand.Index(0), []),
                new(7, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
                new(8, CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]),
            ]),
            _ => new Fixture(Allocated().SetItem(0, Allocated()[0] with
            {
                Operand = new CilOperand.MethodInstance(Constructor with { DeclaringType = array }),
            })),
        };

        Assert.Same(fixture.Body, fixture.Rewrite());
    }

    [Theory]
    // Accepted; a value-type constrained type; a callee with an argument, so
    // the address is a by-reference argument; a branch to the prefix or to the
    // call; a static callee; a prefix at the start of the body; an address
    // with nothing after it.
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void AConstrainedCallOnAnExactLocalLoadsTheReference(int shape, bool devirtualized)
    {
        var callee = shape switch
        {
            2 => WithArgument,
            5 => Method(8, Base, "S", isStatic: true),
            _ => Virtual,
        };
        ImmutableArray<Step> steps =
        [
            new(0, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
            new(1, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
            new(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0), []),
            new(3, CilOperation.Constrained, new CilOperand.TypeIdentity(shape == 1 ? Value : Derived), [CliValueKind.ManagedAddress]),
            new(4, CilOperation.CallVirtual, new CilOperand.MethodInstance(callee), [CliValueKind.ManagedAddress]),
            new(5, CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]),
        ];
        steps = shape switch
        {
            3 or 4 => steps.Add(new(6, CilOperation.Branch, new CilOperand.BranchTarget(shape), [])),
            6 => [new(0, CilOperation.Constrained, new CilOperand.TypeIdentity(Derived), [CliValueKind.ManagedReference]),
                new(1, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
                .. Allocated().Select(static step => step with { Offset = step.Offset + 2 })],
            7 => [.. steps.Take(3), new(3, CilOperation.Pop, new CilOperand.None(), [CliValueKind.ManagedAddress]),
                new(4, CilOperation.Return, new CilOperand.None(), [])],
            _ => steps,
        };
        var fixture = new Fixture(steps);

        var result = fixture.Rewrite();

        if (devirtualized)
        {
            Assert.Equal([CilOperation.LoadLocal, CilOperation.Nop, CilOperation.Call],
                result.Instructions.Skip(2).Take(3).Select(static instruction => instruction.Operation));
            Assert.Equal(new CilOperand.Index(0), result.Instructions[2].Operand);
            Assert.Equal(new CilOperand.MethodInstance(Implementation), result.Instructions[4].Operand);
        }
        else if (shape == 6)
        {
            // Only the ordinary call after the prefix becomes direct.
            Assert.Equal(CilOperation.CallVirtual, result.Instructions[1].Operation);
            Assert.Equal(CilOperation.Call, result.Instructions[5].Operation);
        }
        else
        {
            Assert.Same(fixture.Body, result);
        }
    }

    [Fact]
    public void AStoreMustRunBeforeEveryLoadThatReachesTheCall()
    {
        // The load comes first in the same block, or the store runs on only one path.
        var later = new Fixture(
        [
            new(0, CilOperation.LoadLocal, new CilOperand.Index(0), []),
            new(1, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
            new(2, CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
            new(3, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
            new(4, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
            new(5, CilOperation.Return, new CilOperand.None(), []),
        ]);
        var branched = new Fixture(
        [
            new(0, CilOperation.LoadArgument, new CilOperand.Index(0), []),
            new(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4), [CliValueKind.I4]),
            new(2, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
            new(3, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
            new(4, CilOperation.LoadLocal, new CilOperand.Index(0), []),
            new(5, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
            new(6, CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]),
        ]);
        var dominated = new Fixture(
        [
            new(0, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
            new(1, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
            new(2, CilOperation.LoadArgument, new CilOperand.Index(0), []),
            new(3, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(6), [CliValueKind.I4]),
            new(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1), []),
            new(5, CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
            new(6, CilOperation.LoadLocal, new CilOperand.Index(0), []),
            new(7, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
            new(8, CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]),
        ]);

        Assert.Same(later.Body, later.Rewrite());
        Assert.Same(branched.Body, branched.Rewrite());
        Assert.Equal(CilOperation.Call, dominated.Rewrite().Instructions[7].Operation);
    }

    // foreach over an int[] seen as IEnumerable<int>: the array's helper for
    // the interface reads the array's length first, so the call goes to it
    // directly and still throws for a null array.
    [Fact]
    public void ArrayReceiverCallsTheArrayHelperDirectly()
    {
        var fixture = new Fixture(ArrayReceiver()) { LocalTypes = [Integers], Target = ArrayHelper };

        var rewritten = fixture.Rewrite();

        Assert.Equal(CilOperation.Call, rewritten.Instructions[1].Operation);
        Assert.Same(ArrayHelper, Assert.IsType<CilOperand.MethodInstance>(rewritten.Instructions[1].Operand).Value);
    }

    // A helper that might return before touching the array would not throw
    // for null as the interface call does.
    [Fact]
    public void ArrayReceiverKeepsTheInterfaceCallWhenTheHelperMightNotReadTheArray()
    {
        var fixture = new Fixture(ArrayReceiver()) { LocalTypes = [Integers], Target = ArrayHelper, HelperReadsLength = false };

        Assert.Same(fixture.Body, fixture.Rewrite());
    }

    // A local of an interface type may hold anything implementing it.
    [Fact]
    public void InterfaceTypedLocalIsNotAnArrayReceiver()
    {
        var fixture = new Fixture(ArrayReceiver()) { Target = ArrayHelper };

        Assert.Same(fixture.Body, fixture.Rewrite());
    }

    [Fact]
    public void RejectsMissingDependenciesAndInputs()
    {
        var fixture = new Fixture(Allocated());
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var dominance = new ControlFlowDominanceAnalyzer();

        Assert.Throws<ArgumentNullException>(() => new ExactReceiverDevirtualizer(null!, fixture, fixture, fixture, graphs, fixture, dominance, true));
        Assert.Throws<ArgumentNullException>(() => new ExactReceiverDevirtualizer(fixture, null!, fixture, fixture, graphs, fixture, dominance, true));
        Assert.Throws<ArgumentNullException>(() => new ExactReceiverDevirtualizer(fixture, fixture, null!, fixture, graphs, fixture, dominance, true));
        Assert.Throws<ArgumentNullException>(() => new ExactReceiverDevirtualizer(fixture, fixture, fixture, fixture, null!, fixture, dominance, true));
        Assert.Throws<ArgumentNullException>(() => new ExactReceiverDevirtualizer(fixture, fixture, fixture, fixture, graphs, null!, dominance, true));
        Assert.Throws<ArgumentNullException>(() => new ExactReceiverDevirtualizer(fixture, fixture, fixture, fixture, graphs, fixture, null!, true));
        Assert.Throws<ArgumentNullException>(() => new ExactReceiverDevirtualizer(fixture, fixture, fixture, null!, graphs, fixture, dominance, true));
        var devirtualizer = new ExactReceiverDevirtualizer(fixture, fixture, fixture, fixture, graphs, fixture, dominance, true);
        Assert.Throws<ArgumentNullException>(() => devirtualizer.Rewrite(null!, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        Assert.Throws<ArgumentNullException>(() => devirtualizer.Rewrite(fixture.Body, null!));
    }

    // new Derived(); stored once; its virtual call.
    private static ImmutableArray<Step> Allocated() =>
    [
        new(0, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor), []),
        new(1, CilOperation.StoreLocal, new CilOperand.Index(0), [CliValueKind.ManagedReference]),
        new(2, CilOperation.LoadLocal, new CilOperand.Index(0), []),
        new(3, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
        new(4, CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
        new(5, CilOperation.Return, new CilOperand.None(), []),
    ];

    // An array local's interface call.
    private static ImmutableArray<Step> ArrayReceiver() =>
    [
        new(0, CilOperation.LoadLocal, new CilOperand.Index(0), []),
        new(1, CilOperation.CallVirtual, new CilOperand.MethodInstance(Virtual), [CliValueKind.ManagedReference]),
        new(2, CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
        new(3, CilOperation.Return, new CilOperand.None(), []),
    ];

    private static MethodInstanceModel Method(int token, CliTypeIdentity owner, string name,
        bool isVirtual = false, bool isStatic = false, bool body = true, CliValueKind[]? parameters = null)
    {
        var signature = MethodSignatureModel.Create(name == ".ctor" ? CliValueKind.Void : CliValueKind.I4, parameters ?? []);
        return new(new(new(Assembly, 0x06000000 + token), new(Assembly, 0x02000001), name, isStatic, signature, body ? 1 : 0)
        {
            IsVirtual = isVirtual,
        }, owner, [], signature);
    }

    private sealed record Step(int Offset, CilOperation Operation, CilOperand Operand, ImmutableArray<CliValueKind> Stack);

    private sealed class Fixture(ImmutableArray<Step> steps) :
        ICalledMethodResolver, IDispatchTargetResolver, IMethodBodyReader, IFieldRepository, ITypedStackValidator
    {
        private CilMethodBody? _body;

        public CilMethodBody Body => _body ??= new(Method(9, Base, "Run", isStatic: true).Definition, 4, [CliValueKind.ManagedReference],
            [.. steps.Select(step => new CilInstruction(step.Offset, step.Offset + 1, step.Operation, step.Operand))])
        {
            LocalSignatureTypes = LocalTypes,
        };
        public ImmutableArray<CliTypeIdentity> LocalTypes { get; init; } = [Base];
        public bool HelperReadsLength { get; init; } = true;
        public bool Enabled { get; init; } = true;
        public bool KnownStacks { get; init; } = true;
        public int? MissingStackAt { get; init; }
        public MethodInstanceModel? Target { get; init; } = Implementation;
        public int Validations { get; private set; }

        private ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks => steps
            .Where(step => step.Offset != MissingStackAt)
            .ToImmutableDictionary(step => step.Offset, step => step.Stack);

        public CilMethodBody Rewrite() => new ExactReceiverDevirtualizer(this, this, this, this,
            new ControlFlowGraphBuilderFactory().Create(), this, new ControlFlowDominanceAnalyzer(), Enabled)
            .Rewrite(Body, KnownStacks ? Stacks : ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;

        public DispatchTargetModel? Resolve(MethodInstanceModel declaration, CliTypeIdentity receiver) =>
            Target is null || !receiver.Equals(Derived) && receiver.Shape != CliTypeShape.SzArray ? null : new(receiver, Target);

        // The array helper: array.Length first, or a constant first.
        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => new(method.Definition, 2, [],
            HelperReadsLength
                ?
                [
                    new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new(2, 3, CilOperation.LoadArrayLength, new CilOperand.None()),
                    new(3, 4, CilOperation.Pop, new CilOperand.None()),
                    new(4, 5, CilOperation.Return, new CilOperand.None()),
                ]
                :
                [
                    new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                    new(1, 2, CilOperation.Return, new CilOperand.None()),
                ]);

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => throw new NotSupportedException();

        // These bodies load no fields.
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
        {
            Validations++;
            return new(graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, Stacks);
        }
    }
}
