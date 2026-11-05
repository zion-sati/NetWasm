using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A member a fact may lack fingerprints as none, a named one by its name.
public sealed class UnitFingerprintMemberTests
{
    private static readonly MethodDefinitionModel Definition = new(new EntityKey(UnitActorFixture.Assembly, 0x06000001),
        new EntityKey(UnitActorFixture.Assembly, 0x02000001), "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 0);

    private static UnitFingerprint Of(Action<UnitFingerprintBuilder> add)
    {
        var builder = new UnitFingerprintBuilder();
        add(builder);
        return builder.Finish();
    }

    [Fact]
    public void AMethodOrFieldFingerprintsAsItsNameOrNone()
    {
        var method = new MethodInstanceModel(Definition, UnitActorFixture.Program, [], Definition.Signature);
        var field = new FieldInstanceModel(new FieldDefinitionModel(new EntityKey(UnitActorFixture.Assembly, 0x04000001),
            new EntityKey(UnitActorFixture.Assembly, 0x02000001), "count", CliValueKind.I4, IsStatic: false),
            UnitActorFixture.Program, CliTypeIdentity.Primitive("i4", CliValueKind.I4));

        Assert.Equal(Of(builder => builder.Add(method.CanonicalName)), Of(builder => builder.Add(method)));
        Assert.Equal(Of(builder => builder.Add(field.CanonicalName)), Of(builder => builder.Add(field)));
        Assert.Equal(Of(builder => builder.Add((string?)null)), Of(builder => builder.Add((MethodInstanceModel?)null)));
        Assert.Equal(Of(builder => builder.Add((string?)null)), Of(builder => builder.Add((FieldInstanceModel?)null)));
    }
}

// An exact type an instruction names is a type the unit depends on, as any other it names.
public sealed class UnitExactTypeTests
{
    private static readonly AssemblyIdentity Shapes = new("Shapes");
    private static readonly CliTypeIdentity Circle = CliTypeIdentity.Named(Shapes, "Shapes", "Circle", isValueType: false);

    private static CilInstruction Naming(CilOperand operand) => new(0, 1, CilOperation.Nop, operand);

    [Fact]
    public void AnExactTypesAssemblyIsOneTheUnitDependsOn()
    {
        var body = UnitActorFixture.Method(naming: new CilOperand.ExactType(Circle));
        var names = new HashSet<string>(StringComparer.Ordinal);

        UnitAssemblies.Collect(body, names);

        Assert.Contains("Shapes", names);
        Assert.Same(Circle, UnitQueryArguments.Of(body).Types[Circle.CanonicalName]);
    }

    // An exact type and a type identity of the same type are different operands.
    [Fact]
    public void AnExactTypeIsNotTheTypeIdentityOfTheSameType()
    {
        var exact = new UnitFingerprintBuilder();
        exact.Add(Naming(new CilOperand.ExactType(Circle)));
        var identity = new UnitFingerprintBuilder();
        identity.Add(Naming(new CilOperand.TypeIdentity(Circle)));

        Assert.NotEqual(exact.Finish(), identity.Finish());
    }

    // A query a method's identities cannot answer is asked with the program's.
    [Fact]
    public void ArgumentsBeforeTheProgramsFallBackToThem()
    {
        var method = UnitQueryArguments.Of(UnitActorFixture.Method());
        var program = new UnitQueryArguments(new Dictionary<string, CliTypeIdentity> { [Circle.CanonicalName] = Circle },
            new Dictionary<string, FieldInstanceModel>(), new Dictionary<string, MethodInstanceModel>());

        var both = method.Before(program);

        Assert.True(both.Types.ContainsKey(UnitActorFixture.Program.CanonicalName));
        Assert.True(both.Types.ContainsKey(Circle.CanonicalName));
        Assert.False(both.Types.ContainsKey("[Shapes]Shapes.Square"));
    }
}

// The fingerprint names each operand kind; its last kind ends its switch, so a kind added
// to the closed set fails here until the fingerprint names it.
public sealed class UnitFingerprintOperandTests
{
    [Fact]
    public void TheFingerprintNamesEveryOperandKind() =>
        Assert.Equal(
            ["ArrayInitializerData", "BranchTarget", "CallSite", "ConstantF4", "ConstantF8", "ConstantI4", "ConstantI8", "Entity",
                "ExactType", "FieldInstance", "Index", "MethodInstance", "None", "NumericConversion", "ReadOnlySpanData",
                "SwitchTargets", "TypeIdentity", "UserString"],
            typeof(CilOperand).GetNestedTypes().Where(type => type.IsSubclassOf(typeof(CilOperand)) && !type.IsAbstract)
                .Select(type => type.Name).Order(StringComparer.Ordinal));
}

// Unit types read back as written, an assembly without a name included; bytes cut short of
// a primitive's kinds are refused.
public sealed class UnitModelEdgeTests
{
    [Fact]
    public void ATypeOfAnUnnamedAssemblyReadsBack()
    {
        // A name no other test gives a type, so it is this one's own.
        var type = CliTypeIdentity.Named(default, "Shapes", "OfNoAssembly", isValueType: false);
        var writer = new UnitModelWriter();
        writer.Type(type);

        Assert.Same(type, new UnitModelReader(writer.ToArray(), new NoMethods(), new NoFields()).Type());
    }

    [Fact]
    public void APrimitiveCutShortOfItsStackKindIsRefused()
    {
        var writer = new UnitModelWriter();
        writer.Type(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var bytes = writer.ToArray();

        Assert.Throws<InvalidDataException>(() => new UnitModelReader(bytes.AsSpan(..^5).ToArray(), new NoMethods(), new NoFields()).Type());
    }

    internal sealed class NoMethods : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }

    internal sealed class NoFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();
    }
}

// A site's caller is usually its key's and is then not written; one that differs is. Bytes
// that name a text the key does not hold, or none, are refused; a root map keeps its
// anchors' types.
public sealed class UnitAnswerTextTests
{
    private const string Key = "Program::Run@00000004";
    private static readonly CliTypeIdentity Shape = CliTypeIdentity.Named(new AssemblyIdentity("Program"), "", "Shape", isValueType: false);

    private static object RoundTrip(UnitQueryKind kind, string key, object value) =>
        UnitAnswerCodec.Decode(kind, key, UnitAnswerCodec.Encode(kind, key, value)!, new UnitModelEdgeTests.NoMethods(),
            new UnitModelEdgeTests.NoFields());

    [Theory]
    [InlineData("Program::Run")]
    [InlineData("Program::Other")]
    public void ASitesCallerReadsBackWhetherOrNotItIsItsKeys(string caller)
    {
        var site = new TypeTestSiteModel(caller, 4, Shape, [Shape]);

        var read = (UnitLookup<TypeTestSiteModel>)RoundTrip(UnitQueryKind.TypeTestSite, Key,
            new UnitLookup<TypeTestSiteModel>(true, site));

        Assert.Equal(caller, read.Value!.Caller);
    }

    [Theory]
    [InlineData("usual")]
    [InlineData("missing")]
    public void ACallerTextTheBytesCannotNameIsRefused(string damage)
    {
        var format = UnitAnswerCodec.Encode(UnitQueryKind.StringTypeId, "", 7)![0];
        var writer = new UnitModelWriter();
        writer.Byte(format);
        writer.Bool(true);
        // The caller is its key's, but the key names none; or it is not, but no text follows.
        writer.Bool(damage == "usual");
        if (damage == "missing") writer.String(null);
        writer.Int(4);
        writer.Type(Shape);
        writer.Types([Shape]);

        Assert.Throws<InvalidDataException>(() => UnitAnswerCodec.Decode(UnitQueryKind.TypeTestSite,
            damage == "usual" ? "no caller" : Key, writer.ToArray(), new UnitModelEdgeTests.NoMethods(), new UnitModelEdgeTests.NoFields()));
    }

    [Fact]
    public void ARootMapAnswerKeepsItsAnchorsTypes()
    {
        var method = UnitActorFixture.Method();
        var map = UnitActorFixture.RootMap(method, anchor: UnitActorFixture.Point);

        var read = (UnitLookup<MethodRootMap>)RoundTrip(UnitQueryKind.RootMap, "key", new UnitLookup<MethodRootMap>(true, map));

        Assert.Equal(map.AnchorPlan.Anchors, read.Value!.AnchorPlan.Anchors);
    }

    [Fact]
    public void ARootMapAnswerWhoseAnchorTypeIsNotKeptIsRefused()
    {
        var method = UnitActorFixture.Method();
        var map = UnitActorFixture.RootMap(method, anchor: UnitActorFixture.Point);
        var format = UnitAnswerCodec.Encode(UnitQueryKind.StringTypeId, "", 7)![0];
        var writer = new UnitModelWriter();
        writer.Byte(format);
        writer.Bool(true);
        writer.Entity(map.Method);
        writer.Bytes(UnitRootMapFormat.Write(map).AsSpan());
        writer.Int(0);

        Assert.Throws<InvalidDataException>(() => UnitAnswerCodec.Decode(UnitQueryKind.RootMap, "key", writer.ToArray(),
            new UnitModelEdgeTests.NoMethods(), new UnitModelEdgeTests.NoFields()));
    }
}

// The last compile's answers replay only as they were kept: of the asked type, and kept.
public sealed class UnitAnswersReplayTests
{
    private static UnitAnswers Answers(object? value) => UnitAnswers.Over(UnitActorFixture.Store(new CompiledUnit("Program::Run",
        new(1, 1), [new(UnitQueryKind.StringTypeId, "", new(5, 6)) { Value = value }], [0x00, 0x0B], [], 1) { FunctionName = "f" }),
        new UnitModelEdgeTests.NoMethods(), new UnitModelEdgeTests.NoFields());

    [Fact]
    public void AKeptAnswerReplaysAsItsType()
    {
        Assert.Equal(7, Answers(7).Get<int>(UnitQueryKind.StringTypeId, ""));
        Assert.Contains("was kept as Int32, not String", Assert.Throws<UnitReplayMissException>(() =>
            Answers(7).Get<string>(UnitQueryKind.StringTypeId, "")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnswerNotKeptCannotBeReplayed() =>
        Assert.Contains("kept no answer", Assert.Throws<UnitReplayMissException>(() =>
            Answers(null).Get<int>(UnitQueryKind.StringTypeId, "")).Message, StringComparison.Ordinal);
}

// The replayed and recording views of the target answer as their queries do.
public sealed class UnitTargetViewEdgeTests
{
    private static UnitAnswers Given(UnitQueryKind kind, string key, object value) =>
        UnitAnswers.Over(UnitStore.Empty, new UnitModelEdgeTests.NoMethods(), new UnitModelEdgeTests.NoFields())
            .Overlaid(new Dictionary<(UnitQueryKind Kind, string Key), object> { [(kind, key)] = value },
                new HashSet<string>(StringComparer.Ordinal));

    [Fact]
    public void AReplayedSiteTheLastCompileDidNotFindIsNotThere()
    {
        var sites = new ReplayDictionary<string, DispatchCallSiteModel>(Given(UnitQueryKind.DispatchCallSite, "M@00000004",
            new UnitLookup<DispatchCallSiteModel>(false, null)), UnitQueryKind.DispatchCallSite, static key => key);

        Assert.Equal("M@00000004", Assert.Throws<KeyNotFoundException>(() => sites["M@00000004"]).Message);
    }

    [Fact]
    public void AWholeListEnumeratesAsItsAnswer()
    {
        System.Collections.IEnumerable replayed = new WholeReplayList<int>(
            Given(UnitQueryKind.StringTypeId, "", (IReadOnlyList<int>)[1, 2]), UnitQueryKind.StringTypeId);
        var log = new UnitDependencyLog();
        log.Begin();
        System.Collections.IEnumerable recorded = new WholeRecordingList<int>([3, 4], log, UnitQueryKind.StringTypeId,
            static _ => new UnitFingerprint(9, 9));

        Assert.Equal([1, 2], Enumerated(replayed));
        Assert.Equal([3, 4], Enumerated(recorded));
        Assert.Equal(UnitQueryKind.StringTypeId, Assert.Single(log.End()).Kind);
    }

    // As a non-generic enumerable; LINQ would ask for the generic enumerator.
    private static List<int> Enumerated(System.Collections.IEnumerable items)
    {
        var values = new List<int>();
        var enumerator = items.GetEnumerator();
        while (enumerator.MoveNext()) values.Add((int)enumerator.Current!);
        return values;
    }

    [Fact]
    public void TheManagedThrowFunctionIsTheResolversOwn() =>
        Assert.Equal(OptionalFunctionIndex.At(12),
            new RecordingFunctionIndexResolver(new Throwing(), new UnitDependencyLog()).ManagedThrowFunction);

    [Fact]
    public void ARecordingTypeLayoutProviderNeedsItsProviderAndRecorder()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordingTypeLayoutProvider(null!, new UnitDependencyLog()));
        Assert.Throws<ArgumentNullException>(() => new RecordingTypeLayoutProvider(new NoLayouts(), null!));
    }

    // A manifest whose segment is gone finds none of its units.
    [Fact]
    public void AStoreInMemoryWithoutItsSegmentFindsNoUnits()
    {
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", new(1, null,
            UnitManifestFormat.Write([new(1, 1, 64)], [new(UnitStoreFormat.HashOf("Program::Run"), 0, 64)], 2),
            new HashSet<int> { 1 }, ""));

        using var store = new MemoryUnitStoreLoader(memory).Load("app");

        Assert.Null(store.Find("Program::Run"));
    }

    private sealed class Throwing : IFunctionIndexResolver
    {
        public OptionalFunctionIndex ManagedThrowFunction => OptionalFunctionIndex.At(12);
        public int Resolve(EntityKey method) => throw new NotSupportedException();
        public int Resolve(string method) => throw new NotSupportedException();
        public int Resolve(MethodInstanceModel method) => throw new NotSupportedException();
        public int Resolve(InteropImportKey import) => throw new NotSupportedException();
    }

    private sealed class NoLayouts : ITypeLayoutProvider
    {
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => throw new NotSupportedException();
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout) => throw new NotSupportedException();
        public ObjectLayout GetObjectLayout(EntityKey type) => throw new NotSupportedException();
        public int StringTypeId => throw new NotSupportedException();
        public int TypeTypeId => throw new NotSupportedException();
    }
}
