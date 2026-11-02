using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Allocations;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.Allocations;

public sealed class UnusedAllocationEliminatorTests
{
    private static readonly AssemblyIdentity Assembly = new("AllocationFixture");
    private static readonly CliTypeIdentity ObjectType = CliTypeIdentity.Named(Assembly, "System", "Object", false);
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "", "Owner", false);
    private static readonly CliTypeIdentity Value = CliTypeIdentity.Named(Assembly, "", "Value", true);
    private static readonly CliTypeIdentity Static = CliTypeIdentity.Named(Assembly, "", "Static", false);
    private static readonly MethodInstanceModel ObjectConstructor = Constructor(1, ObjectType, 0);
    private static readonly CilOperand Field = new CilOperand.Entity(new(Assembly, 0x04000001));

    [Fact]
    public void RemovesAnAllocationThatOnlyFedUnreadLocals()
    {
        // new Owner() stored, copied into an unread local, then a constant.
        var constructor = Constructor(2, Owner, 0);
        var fixture = new Fixture(Constructs(constructor, [I(CilOperation.Nop), .. Chain()], 0));
        var body = Body(
            I(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            I(CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            I(CilOperation.Return));

        var result = fixture.Rule().Rewrite(body);

        Assert.All(result.Instructions.Take(4), static instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
        Assert.Equal([CilOperation.LoadInt32, CilOperation.Return], result.Instructions.Skip(4).Select(static instruction => instruction.Operation));
    }

    [Theory]
    // The constructor's arguments are dropped in place of the object: a call's
    // result is popped, and a constant then disappears too.
    [InlineData(1, false, CilOperation.Pop, CilOperation.Nop)]
    [InlineData(2, false, CilOperation.Pop, CilOperation.Pop)]
    [InlineData(1, true, CilOperation.Nop, CilOperation.Nop)]
    [InlineData(2, true, CilOperation.Nop, CilOperation.Nop)]
    public void DropsTheConstructorArguments(int arguments, bool constant, CilOperation replaced, CilOperation popped)
    {
        var constructor = Constructor(2, Owner, arguments);
        var source = new MethodInstanceModel(Constructor(5, Static, 0).Definition with { Name = "Source", IsStatic = true }, Static, [],
            MethodSignatureModel.Create(CliValueKind.I4));
        var fixture = new Fixture(Constructs(constructor,
            [.. Chain(), Arg(0), Arg(1), I(CilOperation.StoreField, Field)], arguments));
        var body = Body(
            [.. Enumerable.Range(0, arguments).Select(_ => constant
                ? I(CilOperation.LoadInt32, new CilOperand.ConstantI4(7))
                : I(CilOperation.Call, new CilOperand.MethodInstance(source))),
            I(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            I(CilOperation.Pop),
            I(CilOperation.Return)]);

        var result = fixture.Rule().Rewrite(body);

        Assert.Equal(replaced, result.Instructions[arguments].Operation);
        Assert.Equal(popped, result.Instructions[arguments + 1].Operation);
        Assert.All(result.Instructions.Take(arguments), instruction =>
            Assert.Equal(constant ? CilOperation.Nop : CilOperation.Call, instruction.Operation));
    }

    [Theory]
    [InlineData(CilOperation.LoadArgument)]
    [InlineData(CilOperation.LoadNull)]
    [InlineData(CilOperation.Duplicate)]
    [InlineData(CilOperation.LoadInt64)]
    [InlineData(CilOperation.LoadFloat32)]
    [InlineData(CilOperation.LoadFloat64)]
    public void RemovesPoppedPureValues(CilOperation push)
    {
        var fixture = new Fixture([]);
        var body = Body(
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(push, push == CilOperation.LoadArgument ? new CilOperand.Index(0) : new CilOperand.None()),
            I(CilOperation.Pop),
            I(CilOperation.Return));

        var result = fixture.Rule().Rewrite(body);

        Assert.Equal([CilOperation.LoadInt32, CilOperation.Nop, CilOperation.Nop, CilOperation.Return],
            result.Instructions.Select(static instruction => instruction.Operation));
    }

    [Theory]
    // Three arguments, a finalizer, a value type, a constructor with a static
    // store, one with a call that is not a constructor, one that stores the new
    // object into itself, one with a protected region, one without a body,
    // one with a branch, one that leaves a value behind, one that never
    // returns, and a chain deeper than the limit.
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
    // A string's constructor is a placeholder for the compiler's construction.
    [InlineData(12)]
    public void KeepsAllocationsWithObservableEffects(int shape)
    {
        var arguments = shape == 0 ? 3 : 0;
        var owner = shape switch
        {
            2 => Value,
            12 => CliTypeIdentity.Named(Assembly, "System", "String", false),
            _ => Owner,
        };
        var constructor = Constructor(2, owner, arguments, body: shape != 7);
        var other = Constructor(3, Static, 0);
        var helper = new MethodInstanceModel(other.Definition with { Name = "Helper", IsStatic = true }, Static, [], other.Signature);
        ImmutableArray<CilInstruction> body = shape switch
        {
            3 => [.. Chain(), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(CilOperation.StoreStaticField, Field)],
            4 => [.. Chain(), I(CilOperation.Call, new CilOperand.MethodInstance(helper))],
            5 => [.. Chain(), Arg(0), Arg(0), I(CilOperation.StoreField, Field)],
            8 => [.. Chain(), I(CilOperation.Branch, new CilOperand.BranchTarget(9))],
            9 => [.. Chain(), Arg(0)],
            _ => Chain(),
        };
        var constructors = Constructs(constructor, body, arguments, regions: shape == 6, returns: shape != 10);
        if (shape == 11)
        {
            // Each constructor chains to the next, past the depth limit.
            constructors = [.. Enumerable.Range(0, 10).SelectMany(depth =>
                Constructs(Constructor(10 + depth, Owner, 0), depth == 9 ? Chain() : Chain(Constructor(11 + depth, Owner, 0)), 0))];
            constructor = Constructor(10, Owner, 0);
        }
        var fixture = new Fixture(constructors) { Finalizable = shape == 1 };
        var program = Body(
            [.. Enumerable.Range(0, arguments).Select(static _ => I(CilOperation.LoadInt32, new CilOperand.ConstantI4(7))),
            I(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
            I(CilOperation.Pop),
            I(CilOperation.Return)]);

        Assert.Same(program, fixture.Rule().Rewrite(program));
    }

    [Fact]
    public void ChainedConstructorsMustReceiveTheNewObjectAndPlainArguments()
    {
        // The base constructor is passed another object, or the new object as an argument.
        var chained = Constructor(3, ObjectType, 1);
        foreach (var call in new ImmutableArray<CilInstruction>[]
        {
            [Arg(1), Arg(1), I(CilOperation.Call, new CilOperand.MethodInstance(chained))],
            [Arg(0), Arg(0), I(CilOperation.Call, new CilOperand.MethodInstance(chained))],
            [Arg(0), I(CilOperation.Call, new CilOperand.MethodInstance(chained))],
        })
        {
            var constructor = Constructor(2, Owner, 1);
            var fixture = new Fixture([.. Constructs(constructor, call, 1), .. Constructs(chained, Chain(), 1)]);
            var program = Body(
                I(CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                I(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)),
                I(CilOperation.Pop),
                I(CilOperation.Return));

            Assert.Same(program, fixture.Rule().Rewrite(program));
        }
    }

    [Fact]
    public void KeepsReadLocalsAndValuesThatAnotherPathReaches()
    {
        var fixture = new Fixture([]);
        var read = Body(
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(CilOperation.Pop),
            I(CilOperation.Return));
        var reached = Body(
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(CilOperation.Pop),
            I(CilOperation.Pop),
            I(CilOperation.Return),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)));

        Assert.Equal(CilOperation.StoreLocal, fixture.Rule().Rewrite(read).Instructions[1].Operation);
        var result = fixture.Rule().Rewrite(reached);
        Assert.Equal([CilOperation.LoadInt32, CilOperation.Nop, CilOperation.Nop, CilOperation.Pop],
            result.Instructions.Take(4).Select(static instruction => instruction.Operation));
    }

    [Fact]
    public void ValuesBeforeAProtectedRegionBoundaryStay()
    {
        var fixture = new Fixture([]);
        var body = Body(
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(CilOperation.Pop),
            I(CilOperation.Nop),
            I(CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 1, 1, 2, 1, null, null),
                new(CilExceptionRegionKind.Filter, 0, 1, 3, 1, null, 3)],
        };

        Assert.Same(body, fixture.Rule().Rewrite(body));
    }

    // Creating an object of a type with an explicit static constructor, or one
    // whose base constructor is such a type's, runs that static constructor, so
    // the allocation stays even when nothing reads it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAllocationThatInitializesItsTypeStays(bool throughBase)
    {
        var baseConstructor = Constructor(31, Owner, 0);
        var constructor = Constructor(30, Owner, 0);
        var fixture = new Fixture([
            .. Constructs(constructor, Chain(throughBase ? baseConstructor : null), 0),
            .. Constructs(baseConstructor, Chain(), 0)])
        {
            Initializing = [(throughBase ? baseConstructor : constructor).Definition.Key],
        };
        var body = Body(I(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), I(CilOperation.Pop), I(CilOperation.Return));

        Assert.Same(body, fixture.Rule().Rewrite(body));
    }

    [Fact]
    public void DisabledRuleAndMissingInputs()
    {
        var fixture = new Fixture([]);
        var body = Body(I(CilOperation.LoadNull), I(CilOperation.Pop), I(CilOperation.Return));

        Assert.Same(body, new UnusedAllocationEliminator(fixture, fixture, fixture, fixture, enabled: false).Rewrite(body));
        Assert.Throws<ArgumentNullException>(() => new UnusedAllocationEliminator(null!, fixture, fixture, fixture, true));
        Assert.Throws<ArgumentNullException>(() => new UnusedAllocationEliminator(fixture, null!, fixture, fixture, true));
        Assert.Throws<ArgumentNullException>(() => new UnusedAllocationEliminator(fixture, fixture, null!, fixture, true));
        Assert.Throws<ArgumentNullException>(() => new UnusedAllocationEliminator(fixture, fixture, fixture, null!, true));
        Assert.Throws<ArgumentNullException>(() => fixture.Rule().Rewrite(null!));
    }

    // Offsets are assigned when the body is built.
    private static CilInstruction I(CilOperation operation, CilOperand? operand = null) =>
        new(0, 1, operation, operand ?? new CilOperand.None());

    private static CilInstruction Arg(int index) => I(CilOperation.LoadArgument, new CilOperand.Index(index));

    // ldarg.0; call base constructor.
    private static ImmutableArray<CilInstruction> Chain(MethodInstanceModel? chained = null) =>
        [Arg(0), I(CilOperation.Call, new CilOperand.MethodInstance(chained ?? ObjectConstructor))];

    private static CilMethodBody Body(params CilInstruction[] instructions) =>
        new(Constructor(99, Owner, 0).Definition, 4, [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [.. instructions.Select(static (instruction, index) => instruction with { Offset = index, NextOffset = index + 1 })]);

    private static ImmutableArray<(MethodInstanceModel Constructor, CilMethodBody Body)> Constructs(
        MethodInstanceModel constructor, ImmutableArray<CilInstruction> instructions, int arguments,
        bool regions = false, bool returns = true) =>
        [(constructor, Body([.. instructions, .. returns ? [I(CilOperation.Return)] : Array.Empty<CilInstruction>()]) with
        {
            ExceptionRegions = regions ? [new(CilExceptionRegionKind.Finally, 0, 1, 1, 1, null, null)] : [],
        })];

    private static MethodInstanceModel Constructor(int token, CliTypeIdentity owner, int arguments, bool body = true)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void, [.. Enumerable.Repeat(CliValueKind.I4, arguments)]);
        return new(new(new(Assembly, 0x06000000 + token), new(Assembly, 0x02000001), ".ctor", false, signature, body ? 1 : 0),
            owner, [], signature);
    }

    private sealed class Fixture(ImmutableArray<(MethodInstanceModel Constructor, CilMethodBody Body)> constructors) :
        IMethodBodyReader, ICalledMethodResolver, IFinalizerResolver, IMethodInitializationTriggerClassifier
    {
        public bool Finalizable { get; init; }

        // Constructors of types with an explicit static constructor.
        public ImmutableHashSet<EntityKey> Initializing { get; init; } = [];

        public UnusedAllocationEliminator Rule() => new(this, this, this, this, enabled: true);

        public bool Classify(MethodDefinitionModel method) => Initializing.Contains(method.Key);

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => throw new InvalidOperationException();

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) =>
            method.Equals(ObjectConstructor)
                ? Body([.. Array.Empty<CilInstruction>(), I(CilOperation.Return)])
                : constructors.Single(entry => entry.Constructor.Equals(method)).Body;

        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;

        public MethodInstanceModel? Resolve(CliTypeIdentity allocatedType) => Finalizable ? ObjectConstructor : null;
    }
}
