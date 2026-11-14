using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests.Analysis;

using static Constants.ConstantRewriteTestSupport;

// The rules that apply ILLink.Substitutions.xml method stubs as ILLink does, and drop what a folded check leaves unread.
public sealed class FeatureSwitchStubRuleTests
{
    private const string Switch = "Fixture.IsSupported";
    private static readonly CliTypeIdentity Boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly EntityKey FieldKey = new(Assembly, 0x04000001);
    private static readonly ImmutableDictionary<string, bool> Off = ImmutableDictionary<string, bool>.Empty.Add(Switch, false);
    private static readonly ImmutableArray<FeatureSwitchStub> Stubbed = [new([(Switch, false)], true)];

    [Fact]
    public void LoadFinderFindsLoadsWithoutEffects()
    {
        var facts = new MethodFacts();
        facts.Fields[FieldKey] = new(FieldKey, TypeKey, "Log", CliValueKind.ManagedReference, true);
        var finder = new EffectFreeLoadFinder(facts, facts, Constructors(facts));

        Assert.Throws<ArgumentNullException>(() => finder.FindStart(null!, 0));
        Assert.Equal(-1, finder.FindStart([], 0));
        foreach (var operation in new[]
        {
            CilOperation.LoadArgument, CilOperation.LoadLocal, CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32,
            CilOperation.LoadFloat64, CilOperation.LoadNull, CilOperation.LoadString,
        })
        {
            Assert.Equal(0, finder.FindStart([Instruction(0, operation), Instruction(1, CilOperation.Pop)], 1));
        }
        // Unchecked conversions of a load belong to it; a checked one, or a load with effects, does not qualify.
        foreach (var conversion in new[]
        {
            CilOperation.ConvertInt32, CilOperation.ConvertInt32Unsigned, CilOperation.ConvertInt64, CilOperation.ConvertInt64Unsigned,
            CilOperation.ConvertNativeInt, CilOperation.ConvertNativeUInt, CilOperation.ConvertFloat32, CilOperation.ConvertFloat64,
            CilOperation.ConvertFloatUnsigned,
        })
        {
            Assert.Equal(0, finder.FindStart([Instruction(0, CilOperation.LoadInt32), Instruction(1, conversion)], 2));
        }
        Assert.Equal(-1, finder.FindStart([Instruction(0, CilOperation.LoadInt32), Instruction(1, CilOperation.ConvertNumeric)], 2));
        Assert.Equal(-1, finder.FindStart([Instruction(0, CilOperation.ConvertInt64)], 1));
        Assert.Equal(-1, finder.FindStart([Instruction(0, CilOperation.Call)], 1));

        // A static field's load qualifies where its type's initialization is optional: beforefieldinit, or no .cctor.
        CilInstruction[] field = [Instruction(0, CilOperation.LoadStaticField, new CilOperand.Entity(FieldKey))];
        Assert.Equal(0, finder.FindStart(field, 1));
        facts.HasInitializer = true;
        Assert.Equal(-1, finder.FindStart(field, 1));
        facts.Definition = facts.Definition with { IsBeforeFieldInit = true };
        Assert.Equal(0, finder.FindStart(field, 1));
    }

    [Fact]
    public void RulesRejectMissingCollaborators()
    {
        var facts = new MethodFacts();
        var graph = Graph(Body(Instruction(0, CilOperation.Return)));
        var loads = new EffectFreeLoadFinder(facts, facts, Constructors(facts));
        var values = Values(Off);
        Assert.Throws<ArgumentNullException>(() => new EffectFreeLoadFinder(null!, facts, Constructors(facts)));
        Assert.Throws<ArgumentNullException>(() => new EffectFreeLoadFinder(facts, null!, Constructors(facts)));
        Assert.Throws<ArgumentNullException>(() => new EffectFreeLoadFinder(facts, facts, null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchCallRewriter(null!, facts, loads, values));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchCallRewriter(graph, null!, loads, values));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchCallRewriter(graph, facts, null!, values));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchCallRewriter(graph, facts, loads, null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchCallRewriter(graph, facts, loads, values).Rewrite(null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchBodyStubRewriter(null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchBodyStubRewriter(values).Rewrite(null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchStubValueSelector(null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchStubValueSelector(Off).Select(null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchGetterValueSelector(null!, values));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchGetterValueSelector(Off, null!));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchGetterValueSelector(Off, values).Select(null!));
        Assert.Throws<ArgumentNullException>(() => new PreciseStaticConstructorClassifier(null!));
        Assert.Throws<ArgumentNullException>(() => Constructors(facts).Classify(null!));
        Assert.Throws<ArgumentNullException>(() => new UnreadLocalStoreRewriter(null!, loads));
        Assert.Throws<ArgumentNullException>(() => new UnreadLocalStoreRewriter(graph, null!));
        Assert.Throws<ArgumentNullException>(() => new UnreadLocalStoreRewriter(graph, loads).Rewrite(null!));
    }

    // A stubbed instance method called on a static field folds to its value, and the loads of its receiver and
    // arguments go, a converted constant included.
    [Fact]
    public void AStubbedInstanceCallFoldsWithItsLoads()
    {
        var target = Target(isStatic: false, parameters: 2);
        var body = Body(
            Instruction(0, CilOperation.LoadStaticField, new CilOperand.Entity(FieldKey)),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            Instruction(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(3, CilOperation.ConvertInt64),
            Instruction(4, CilOperation.CallVirtual, new CilOperand.MethodInstance(target)),
            Instruction(5, CilOperation.Return));

        var rewritten = Calls(body).Rewrite(body);

        Assert.Equal(
            [CilOperation.Nop, CilOperation.Nop, CilOperation.Nop, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.Return],
            rewritten.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(1), rewritten.Instructions[4].Operand);
    }

    // A static, parameterless stub and a feature switch's getter fold in place.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaticSwitchesFoldInPlace(bool getter)
    {
        var target = getter
            ? Target(isStatic: true, stubs: [], featureSwitch: Switch)
            : Target(isStatic: true);
        var body = Body(Instruction(0, CilOperation.Call, new CilOperand.MethodInstance(target)), Instruction(1, CilOperation.Return));

        var rewritten = Calls(body).Rewrite(body);

        Assert.Equal(new CilOperand.ConstantI4(getter ? 0 : 1), rewritten.Instructions[0].Operand);
    }

    // A receiver a load with effects pushes, a call that dispatches to an override, another return type, an
    // unresolved call, a stub the switches do not select, or no switches at all keep the call.
    [Fact]
    public void CallsThatCannotFoldStay()
    {
        AssertKept([Instruction(0, CilOperation.Call), Instruction(1, CilOperation.CallVirtual, Operand(Target(isStatic: false)))]);
        AssertKept([Instruction(0, CilOperation.LoadNull), Instruction(1, CilOperation.CallVirtual,
            Operand(Target(isStatic: false) with { Definition = Target(isStatic: false).Definition with { IsVirtual = true } }))]);
        AssertKept([Instruction(0, CilOperation.Call, Operand(Target(isStatic: true, returnsBool: false)))]);
        AssertKept([Instruction(0, CilOperation.Call)]);
        AssertKept([Instruction(0, CilOperation.Call, Operand(Target(isStatic: true, stubs: [new([(Switch, true)], true)])))]);
        AssertKept([Instruction(0, CilOperation.LoadInt32)]);
        var body = Body(Instruction(0, CilOperation.Call, Operand(Target(isStatic: true))), Instruction(1, CilOperation.Return));
        Assert.Same(body, new FeatureSwitchCallRewriter(Graph(body), new MethodFacts(), Loads(),
            Values(ImmutableDictionary<string, bool>.Empty)).Rewrite(body));

        // A final override dispatches nowhere else, so it folds.
        var final = Target(isStatic: false) with { Definition = Target(isStatic: false).Definition with { IsVirtual = true, IsFinal = true } };
        var sealedCall = Body(Instruction(0, CilOperation.LoadNull), Instruction(1, CilOperation.CallVirtual, Operand(final)),
            Instruction(2, CilOperation.Return));
        Assert.Equal(CilOperation.LoadInt32, Calls(sealedCall).Rewrite(sealedCall).Instructions[1].Operation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStubbedMethodHasTheStubsBody(bool value)
    {
        var body = Body(Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)), Instruction(1, CilOperation.Return)) with
        {
            Method = Target(isStatic: false, stubs: [new([(Switch, false)], value)]).Definition,
        };

        var stubbed = new FeatureSwitchBodyStubRewriter(Values(Off)).Rewrite(body);

        Assert.Equal([CilOperation.LoadInt32, CilOperation.Return], stubbed.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(value ? 1 : 0), stubbed.Instructions[0].Operand);
        Assert.Empty(stubbed.Locals);
    }

    [Fact]
    public void AStubbedVoidMethodReturnsAtOnce()
    {
        var method = Target(isStatic: false);
        var body = Body(Instruction(0, CilOperation.Call), Instruction(1, CilOperation.Return)) with
        {
            Method = method.Definition with { Signature = MethodSignatureModel.Create(CliValueKind.Void) },
        };

        Assert.Equal([CilOperation.Return],
            new FeatureSwitchBodyStubRewriter(Values(Off)).Rewrite(body).Instructions.Select(instruction => instruction.Operation));
        // Without a stub the switches select, the body stays.
        Assert.Same(body, new FeatureSwitchBodyStubRewriter(Values(ImmutableDictionary<string, bool>.Empty.Add(Switch, true))).Rewrite(body));
        var plain = Body(Instruction(0, CilOperation.Return));
        Assert.Same(plain, new FeatureSwitchBodyStubRewriter(Values(Off)).Rewrite(plain));
    }

    // A store to a local nothing reads goes with the load that pushed its value; a pinned local, a local something
    // reads or takes the address of, and a value a load with effects pushes stay.
    [Fact]
    public void UnreadStoresGoWithTheirLoads()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadStaticField, new CilOperand.Entity(FieldKey)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.Return));
        var rewritten = Stores(body).Rewrite(body);
        Assert.Equal([CilOperation.Nop, CilOperation.Nop, CilOperation.Return], rewritten.Instructions.Select(instruction => instruction.Operation));

        AssertStoreKept(body with { PinnedLocals = [0] });
        AssertStoreKept(Body(Instruction(0, CilOperation.Call), Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.Return)));
        AssertStoreKept(Body(Instruction(0, CilOperation.LoadInt32), Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadLocal, new CilOperand.Index(0)), Instruction(3, CilOperation.Return)));
        AssertStoreKept(Body(Instruction(0, CilOperation.LoadInt32), Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0)), Instruction(3, CilOperation.Return)));
    }

    // The getter link answers for a feature switch's getter whose switch the project sets, and passes every other
    // method on; the stub link selects the first stub whose conditions hold.
    [Fact]
    public void SelectorsChainTheGetterBeforeTheStubs()
    {
        var getter = Target(isStatic: true, stubs: [], featureSwitch: Switch).Definition;
        var unset = Target(isStatic: true, stubs: [], featureSwitch: "Other").Definition;
        var stubbed = Target(isStatic: true, stubs: [new([(Switch, true)], true), new([(Switch, false)], false)]).Definition;
        var values = Values(Off);

        Assert.False(values.Select(getter));
        Assert.Null(values.Select(unset));
        Assert.False(values.Select(stubbed));
        Assert.Null(values.Select(Target(isStatic: true, stubs: []).Definition));
    }

    // A type's static constructor runs at a precise point only where it has one and is not beforefieldinit.
    [Fact]
    public void PreciseStaticConstructorsAreThoseOfTypesThatAreNotBeforeFieldInit()
    {
        var facts = new MethodFacts();
        var classifier = Constructors(facts);
        var withInitializer = facts.Definition with { Methods = [new EntityKey(Assembly, 0x0600ffff)] };

        Assert.False(classifier.Classify(facts.Definition));
        Assert.True(classifier.Classify(withInitializer));
        Assert.False(classifier.Classify(withInitializer with { IsBeforeFieldInit = true }));
    }

    private static MethodInstanceModel Target(bool isStatic, int parameters = 0, bool returnsBool = true,
        ImmutableArray<FeatureSwitchStub>? stubs = null, string? featureSwitch = null)
    {
        var signature = new MethodSignatureModel(returnsBool ? Boolean : Int32, [.. Enumerable.Repeat(Int32, parameters)]);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000010), TypeKey, "IsEnabled", isStatic, signature, 1)
        {
            FeatureSwitchStubs = stubs ?? Stubbed,
            FeatureSwitch = featureSwitch,
        };
        return new(definition, Owner, [], signature);
    }

    private static CilOperand.MethodInstance Operand(MethodInstanceModel method) => new(method);

    private static EffectFreeLoadFinder Loads()
    {
        var facts = new MethodFacts();
        facts.Fields[FieldKey] = new(FieldKey, TypeKey, "Log", CliValueKind.ManagedReference, true);
        return new(facts, facts, Constructors(facts));
    }

    // The chain the compiler composes: a feature switch's getter first, then a method's stubs.
    private static FeatureSwitchGetterValueSelector Values(ImmutableDictionary<string, bool> switches) =>
        new(switches, new FeatureSwitchStubValueSelector(switches));

    private static FeatureSwitchCallRewriter Calls(CilMethodBody body) => new(Graph(body), new MethodFacts(), Loads(), Values(Off));

    private static UnreadLocalStoreRewriter Stores(CilMethodBody body) => new(Graph(body), Loads());

    private static void AssertKept(CilInstruction[] instructions)
    {
        var body = Body([.. instructions, Instruction(instructions.Length, CilOperation.Return)]);
        Assert.Same(body, Calls(body).Rewrite(body));
    }

    private static void AssertStoreKept(CilMethodBody body) => Assert.Same(body, Stores(body).Rewrite(body));
}
