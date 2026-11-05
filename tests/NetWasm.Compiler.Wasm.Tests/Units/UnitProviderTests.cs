using System.Collections;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// The providers a unit's emission asks through: recording ones record each answer under
// the key a replaying compile asks it by, and replaying ones answer from the last compile's
// answers, refusing what they cannot answer one key at a time.
public sealed class UnitProviderTests
{
    private static readonly CliTypeIdentity Point = CliTypeIdentity.Named(
        new AssemblyIdentity("Program"), "", "Point", isValueType: true);
    private static readonly ImmutableArray<ValueTypeDescriptorLayout> Values = [new(Point, 7, 16, 8, 96, 2)];
    private static readonly ImmutableArray<MetadataTypeDescriptorLayout> Metadata = [new(Point, 9)];

    [Fact]
    public void WholeProgramDataIsRecordedOncePerMethodAndAnsweredUnchanged()
    {
        var log = new UnitDependencyLog();
        var data = new RecordingStaticDataLayout(new Data(), log);
        var descriptors = new RecordingTypeDescriptorSource(new Descriptors(), log);

        log.Begin();
        Assert.Equal(256, data.StaticDataEnd);
        Assert.Equal([32, 48], data.StaticRootAddresses);
        Assert.Equal([32, 48], data.StaticRootAddresses);
        Assert.Equal(1024, data.TypeObjectTableAddress);
        Assert.Equal(3, data.TypeObjectCount);
        Assert.Equal(24, data.TypeObjectSize);
        Assert.Equal(Values, descriptors.ValueTypeDescriptors);
        Assert.Equal(Values, descriptors.ValueTypeDescriptors);
        Assert.Equal(Metadata, descriptors.MetadataTypeDescriptors);
        Assert.Equal(Metadata, descriptors.MetadataTypeDescriptors);
        var dependencies = log.End();

        Assert.Equal(
            [
                (UnitQueryKind.StaticDataEnd, ""), (UnitQueryKind.StaticRootAddresses, ""),
                (UnitQueryKind.TypeObjectTable, "address"), (UnitQueryKind.TypeObjectTable, "count"),
                (UnitQueryKind.TypeObjectTable, "size"), (UnitQueryKind.ValueTypeDescriptors, ""),
                (UnitQueryKind.MetadataTypeDescriptors, ""),
            ],
            dependencies.Select(dependency => (dependency.Kind, dependency.Key)).Order());
    }

    [Fact]
    public void RecordedTypeObjectAddressesAreLookedUpByTypeOnly()
    {
        var log = new UnitDependencyLog();
        var addresses = new RecordingStaticDataLayout(new Data(), log).TypeObjectAddresses;

        log.Begin();
        Assert.Equal(128, addresses["Program::Point"]);
        Assert.Throws<KeyNotFoundException>(() => addresses["Program::Missing"]);
        _ = log.End();

        Assert.Throws<NotSupportedException>(() => addresses.Values);
        Assert.Throws<NotSupportedException>(() => addresses.Count);
        Assert.Throws<NotSupportedException>(() => ((IEnumerable)addresses).GetEnumerator());
    }

    [Fact]
    public void ARecordedDictionaryIsLookedUpByKeyOnly()
    {
        var log = new UnitDependencyLog();
        var view = new RecordingDictionary<string, int>(new Dictionary<string, int> { ["a"] = 1 }, log,
            UnitQueryKind.ByRefEffects, key => key, static (ref answer, value) => answer.Add(value));

        Assert.Throws<NotSupportedException>(() => view.Keys);
        Assert.Throws<NotSupportedException>(() => view.GetEnumerator());
        Assert.Throws<NotSupportedException>(() => ((IEnumerable)view).GetEnumerator());
    }

    [Fact]
    public void ARecordedMembershipSetAnswersMembershipOnly()
    {
        var log = new UnitDependencyLog();
        var set = new RecordingMembershipSet<string>(new HashSet<string> { "Program::Point" }, log,
            UnitQueryKind.AllocatingConstructedMethod, item => item);
        string[] other = ["Program::Point"];

        log.Begin();
        Assert.Contains("Program::Point", set);
        Assert.DoesNotContain("Program::Other", set);
        Assert.Equal(["Program::Other", "Program::Point"], log.End().Select(dependency => dependency.Key).Order());
        Assert.True(set.IsReadOnly);
        Action[] refused =
        [
            () => _ = set.Count, () => set.Add("x"), () => ((ICollection<string>)set).Add("x"), set.Clear,
            () => set.CopyTo(new string[1], 0), () => set.Remove("x"), () => set.GetEnumerator(),
            () => ((IEnumerable)set).GetEnumerator(), () => set.ExceptWith(other), () => set.IntersectWith(other),
            () => set.IsProperSubsetOf(other), () => set.IsProperSupersetOf(other), () => set.IsSubsetOf(other),
            () => set.IsSupersetOf(other), () => set.Overlaps(other), () => set.SetEquals(other),
            () => set.SymmetricExceptWith(other), () => set.UnionWith(other),
        ];
        Assert.All(refused, action => Assert.Throws<NotSupportedException>(action));
    }

    [Fact]
    public void ReplayingProvidersAnswerByTheKeysTheirRecordersRecorded()
    {
        var definition = new EntityKey(new AssemblyIdentity("Program"), 0x02000002);
        var answers = Answers(
            ((UnitQueryKind.ObjectLayoutOfDefinition, UnitQueryKeys.Of(definition)), new ObjectLayout(10, 24, [8])),
            ((UnitQueryKind.StaticDataEnd, ""), 256),
            ((UnitQueryKind.StaticRootAddresses, ""), ImmutableArray.Create(32, 48)),
            ((UnitQueryKind.DataSegments, ""), ImmutableArray.Create(new DataSegment(16, [1, 2]))),
            ((UnitQueryKind.TypeObjectTable, "address"), 1024),
            ((UnitQueryKind.TypeObjectTable, "count"), 3),
            ((UnitQueryKind.TypeObjectTable, "size"), 24),
            ((UnitQueryKind.TypeObjectAddress, "Program::Point"), new UnitLookup<int>(true, 128)),
            ((UnitQueryKind.TypeObjectAddress, "Program::Missing"), new UnitLookup<int>(false, 0)),
            ((UnitQueryKind.MemberDescriptorOffsets, "declaring-type-id"), 4),
            ((UnitQueryKind.MemberDescriptorOffsets, "requires-declaring-type"), 8),
            ((UnitQueryKind.ValueTypeDescriptors, ""), Values),
            ((UnitQueryKind.MetadataTypeDescriptors, ""), Metadata));
        var data = new ReplayStaticDataLayout(answers);
        var members = new ReplayMemberDescriptorLayout(answers);
        var descriptors = new ReplayTypeDescriptorSource(answers);

        Assert.Equal(24, new ReplayTypeLayoutProvider(answers).GetObjectLayout(definition).Size);
        Assert.Equal(256, data.StaticDataEnd);
        Assert.Equal([32, 48], data.StaticRootAddresses);
        Assert.Equal(16, Assert.Single(data.DataSegments).Address);
        Assert.Equal(1024, data.TypeObjectTableAddress);
        Assert.Equal(3, data.TypeObjectCount);
        Assert.Equal(24, data.TypeObjectSize);
        Assert.Equal(128, data.TypeObjectAddresses["Program::Point"]);
        Assert.True(data.TypeObjectAddresses.ContainsKey("Program::Point"));
        Assert.Throws<KeyNotFoundException>(() => data.TypeObjectAddresses["Program::Missing"]);
        Assert.Equal(4, members.DeclaringTypeIdOffset);
        Assert.Equal(8, members.RequiresDeclaringTypeOffset);
        Assert.Equal(Values, descriptors.ValueTypeDescriptors);
        Assert.Equal(Metadata, descriptors.MetadataTypeDescriptors);
    }

    [Fact]
    public void ReplayingProvidersMissWhatTheLastCompileDidNotAnswer()
    {
        var answers = Answers();
        var data = new ReplayStaticDataLayout(answers);

        Assert.Throws<UnitReplayMissException>(() => data.StaticDataEnd);
        Assert.Throws<UnitReplayMissException>(() => data.TypeObjectCacheAddress);
        Assert.Throws<UnitReplayMissException>(() => new ReplayMemberDescriptorLayout(answers).GetDescriptorAddress(Field()));
        Assert.Throws<UnitReplayMissException>(() => data.TypeObjectAddresses.Keys);
        Assert.Throws<UnitReplayMissException>(() => data.TypeObjectAddresses.Values);
        Assert.Throws<UnitReplayMissException>(() => data.TypeObjectAddresses.Count);
        Assert.Throws<UnitReplayMissException>(() => data.TypeObjectAddresses.GetEnumerator());
        Assert.Throws<UnitReplayMissException>(() => ((IEnumerable)data.TypeObjectAddresses).GetEnumerator());
    }

    [Fact]
    public void AReplayedMembershipSetAnswersMembershipOnly()
    {
        var set = new ReplayMembershipSet<string>(
            Answers(((UnitQueryKind.Finalizable, "Program::Point"), true)), UnitQueryKind.Finalizable, item => item);
        string[] other = ["Program::Point"];

        Assert.Contains("Program::Point", set);
        Assert.True(set.IsReadOnly);
        Action[] refused =
        [
            () => _ = set.Count, () => set.Add("x"), () => ((ICollection<string>)set).Add("x"), set.Clear,
            () => set.CopyTo(new string[1], 0), () => set.Remove("x"), () => set.GetEnumerator(),
            () => ((IEnumerable)set).GetEnumerator(), () => set.ExceptWith(other), () => set.IntersectWith(other),
            () => set.IsProperSubsetOf(other), () => set.IsProperSupersetOf(other), () => set.IsSubsetOf(other),
            () => set.IsSupersetOf(other), () => set.Overlaps(other), () => set.SetEquals(other),
            () => set.SymmetricExceptWith(other), () => set.UnionWith(other),
        ];
        Assert.All(refused, action => Assert.Throws<UnitReplayMissException>(action));
    }

    [Fact]
    public void ReplayedTargetMapsAnswerByKeyOrWhole()
    {
        var answers = Answers(
            ((UnitQueryKind.DispatchCallSite, "site"), new UnitLookup<int>(true, 5)),
            ((UnitQueryKind.Finalizable, "type"), true),
            ((UnitQueryKind.DelegateTypes, ""), (IReadOnlyList<string>)["Func"]),
            ((UnitQueryKind.CallableMethods, ""), (IReadOnlyDictionary<string, int>)new Dictionary<string, int> { ["m"] = 3 }));
        var maps = new ReplayTargetMaps(answers);
        var keyed = maps.Answer(new Dictionary<string, int>(), UnitQueryKind.DispatchCallSite, key => key,
            static (ref answer, value) => answer.Add(value));
        var set = maps.Answer(new HashSet<string>(), UnitQueryKind.Finalizable, item => item);
        var whole = maps.Answer(new Dictionary<string, int>(), UnitQueryKind.CallableMethods,
            static _ => default(UnitFingerprint));
        var list = maps.Answer((IReadOnlyList<string>)[], UnitQueryKind.DelegateTypes, static _ => default(UnitFingerprint));
        string[] other = ["type"];

        Assert.Equal(5, keyed["site"]);
        Assert.Contains("type", set);
        Assert.Equal(3, whole["m"]);
        Assert.Equal(["m"], whole.Keys);
        Assert.Equal([3], whole.Values);
        var count = whole.Count;
        Assert.Equal(1, count);
        Assert.True(whole.ContainsKey("m"));
        Assert.True(whole.TryGetValue("m", out _));
        Assert.Single(whole);
        Assert.Single((IEnumerable)whole);
        Assert.Equal("Func", list[0]);
        Assert.Single((IEnumerable)list);
        Action[] refused =
        [
            () => _ = keyed.Keys, () => _ = keyed.Values, () => _ = keyed.Count, () => keyed.GetEnumerator(),
            () => ((IEnumerable)keyed).GetEnumerator(), () => _ = set.Count, () => set.GetEnumerator(),
            () => ((IEnumerable)set).GetEnumerator(), () => set.IsProperSubsetOf(other), () => set.IsProperSupersetOf(other),
            () => set.IsSubsetOf(other), () => set.IsSupersetOf(other), () => set.Overlaps(other), () => set.SetEquals(other),
        ];
        Assert.All(refused, action => Assert.Throws<UnitReplayMissException>(action));
    }

    private static UnitAnswers Answers(params ((UnitQueryKind Kind, string Key) Query, object Value)[] given) =>
        UnitAnswers.Over(UnitStore.Empty, new NoMethods(), new NoFields())
            .Overlaid(given.ToDictionary(answer => answer.Query, answer => answer.Value), new HashSet<string>());

    private static FieldInstanceModel Field()
    {
        var assembly = new AssemblyIdentity("Program");
        var definition = new FieldDefinitionModel(new(assembly, 0x04000001), new(assembly, 0x02000002), "Value", Point, false);
        return new(definition, Point, Point);
    }

    private sealed class Data : IStaticDataLayout
    {
        public StringLayout GetStringLayout(string value) => new(64, value.Length, 12);
        public int StaticDataEnd => 256;
        public ImmutableArray<int> StaticRootAddresses => [32, 48];
        public ImmutableArray<DataSegment> DataSegments => [];
        public IReadOnlyDictionary<string, int> TypeObjectAddresses =>
            new Dictionary<string, int> { ["Program::Point"] = 128 };
        public int TypeObjectTableAddress => 1024;
        public int TypeObjectCount => 3;
        public int TypeObjectSize => 24;
    }

    private sealed class Descriptors : ITypeDescriptorSource, IEnumMetadataSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => Values;
        public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors => Metadata;
        public ImmutableArray<EnumMetadataLayout> EnumMetadata => [];
    }

    private sealed class NoMethods : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }

    private sealed class NoFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();
    }
}

// A replayed query's argument is rebuilt from the identities its method names, then from
// the program's; the method's shadow the program's, so the view is looked up, never listed.
public sealed class UnitQueryArgumentsTests
{
    private static readonly CliTypeIdentity Own = CliTypeIdentity.Named(new AssemblyIdentity("Program"), "", "Own", false);
    private static readonly CliTypeIdentity Shared = CliTypeIdentity.Named(new AssemblyIdentity("Program"), "", "Shared", false);

    [Fact]
    public void AMethodsIdentitiesShadowTheProgramsAndAreNeverListed()
    {
        var method = new UnitQueryArguments(new Dictionary<string, CliTypeIdentity> { ["T"] = Own },
            new Dictionary<string, FieldInstanceModel>(), new Dictionary<string, MethodInstanceModel>())
        {
            Definitions = [new(new AssemblyIdentity("Program"), 0x04000001)],
        };
        var program = new UnitQueryArguments(
            new Dictionary<string, CliTypeIdentity> { ["T"] = Shared, ["U"] = Shared },
            new Dictionary<string, FieldInstanceModel>(), new Dictionary<string, MethodInstanceModel>());

        var types = method.Before(program).Types;

        Assert.Same(Own, types["T"]);
        Assert.Same(Shared, types["U"]);
        Assert.True(types.ContainsKey("U"));
        Assert.Throws<KeyNotFoundException>(() => types["V"]);
        Assert.Equal(method.Definitions, method.Before(program).Definitions);
        Assert.Throws<NotSupportedException>(() => types.Keys);
        Assert.Throws<NotSupportedException>(() => types.Values);
        Assert.Throws<NotSupportedException>(() => types.Count);
        Assert.Throws<NotSupportedException>(() => types.GetEnumerator());
        Assert.Throws<NotSupportedException>(() => ((IEnumerable)types).GetEnumerator());
    }
}

// The target views a unit's emission reads: whole maps and lists record one query on any
// read, membership sets one per member, and function indices pass through.
public sealed class UnitTargetViewTests
{
    [Fact]
    public void AWholeMapOrListRecordsItsReadAndAnswersAsItsSource()
    {
        var log = new UnitDependencyLog();
        var map = new WholeRecordingDictionary<string, int>(new Dictionary<string, int> { ["m"] = 3 }, log,
            UnitQueryKind.CallableMethods, static _ => new(1, 1));
        var list = new WholeRecordingList<string>(["Func"], log, UnitQueryKind.DelegateTypes, static _ => new(2, 2));

        log.Begin();
        Assert.Equal(3, map["m"]);
        Assert.Equal(["m"], map.Keys);
        Assert.Equal([3], map.Values);
        Assert.True(map.ContainsKey("m") && map.TryGetValue("m", out _));
        Assert.Single(map);
        Assert.Single((System.Collections.IEnumerable)map);
        Assert.Equal("Func", list[0]);
        Assert.Single(list);
        Assert.Single((System.Collections.IEnumerable)list);
        var count = map.Count;

        Assert.Equal(1, count);
        Assert.Equal([UnitQueryKind.CallableMethods, UnitQueryKind.DelegateTypes],
            log.End().Select(dependency => dependency.Kind).Order());
    }

    [Fact]
    public void ARecordingSetAnswersMembershipOnly()
    {
        var set = new RecordingSet<string>(new HashSet<string> { "type" }, new UnitDependencyLog(),
            UnitQueryKind.Finalizable, item => item);
        string[] other = ["type"];

        Action[] refused =
        [
            () => _ = set.Count, () => set.GetEnumerator(), () => ((System.Collections.IEnumerable)set).GetEnumerator(),
            () => set.IsProperSubsetOf(other), () => set.IsProperSupersetOf(other), () => set.IsSubsetOf(other),
            () => set.IsSupersetOf(other), () => set.Overlaps(other), () => set.SetEquals(other),
        ];
        Assert.All(refused, action => Assert.Throws<NotSupportedException>(action));
    }

    [Fact]
    public void AReplayMissCarriesWhatTheLastCompileDidNotAnswer()
    {
        var inner = new InvalidDataException("damaged");

        Assert.Equal("missed", new UnitReplayMissException("missed", inner).Message);
        Assert.Same(inner, new UnitReplayMissException("missed", inner).InnerException);
        Assert.NotNull(new UnitReplayMissException().Message);
    }

    // An answer given for an edited method's own facts must be of the type asked for, as a
    // kept one must; a reference-typed answer cannot be taken as absent.
    [Fact]
    public void AnAnswerOfAnotherTypeIsAMiss()
    {
        var answers = UnitAnswers.Over(UnitStore.Empty, new NoMethods(), new NoFields()).Overlaid(
            new Dictionary<(UnitQueryKind Kind, string Key), object> { [(UnitQueryKind.StringTypeId, "")] = "two" },
            new HashSet<string> { "caller" });

        Assert.Equal(0, answers.Count);
        Assert.Throws<UnitReplayMissException>(() => answers.Get<int>(UnitQueryKind.StringTypeId, ""));
        Assert.Equal(default, answers.Get<UnitLookup<int>>(UnitQueryKind.DispatchCallSite, "caller@4"));
        Assert.Throws<UnitReplayMissException>(() => answers.Get<string>(UnitQueryKind.TypeTestSite, "caller@8"));
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("count")]
    [InlineData("imported")]
    public void FunctionIndicesOfAnotherFormatReadAsNone(string damage)
    {
        var bytes = UnitFunctionIndices.Write([new("Program::Run", 3)], importedFunctions: 2);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(damage switch
        {
            "magic" => 0,
            "version" => 4,
            "imported" => 8,
            _ => 12,
        }), damage == "version" ? 99 : -1);

        Assert.Empty(UnitFunctionIndices.Read(bytes).Functions);
    }

    private sealed class NoMethods : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }

    private sealed class NoFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();
    }
}

// Under verification each restored or replayed body is emitted too, and any difference from
// emission fails the compile, naming what differs.
public sealed class UnitVerificationTests
{
    private static ManagedBodyEmission Emission(byte[] body, Func<WasmFunctionDefinition, WasmFunctionDefinition>? function = null,
        string environment = "", int instructions = 3, int blocks = 1) => new(
        (function ?? (definition => definition))(new("Program_Run", WasmFunctionType.Create(CliValueKind.I4), body)),
        environment, FilterEnvironmentLayout.Empty,
        new("Program::Run", "Program::Run", new(body, instructions, 0, null, "Program::Run", FilterEnvironmentLayout.Empty,
            ImmutableDictionary<int, int>.Empty.Add(0, blocks))));

    [Fact]
    public void ARestoredUnitThatMatchesEmissionIsTaken()
    {
        var emitted = Emission([0x00, 0x0B]);
        var restored = Emission([0x00, 0x0B]);

        Assert.Same(restored, NetWasm.Compiler.Wasm.Emission.Methods.ManagedBodyBatchEmitter.Verified("Program::Run", restored, emitted));
        Assert.Same(emitted, NetWasm.Compiler.Wasm.Emission.Methods.ManagedBodyBatchEmitter.Verified("Program::Run", null, emitted));
    }

    public static TheoryData<string> Differences => new()
    {
        "name", "type", "body", "inlining", "environment key", "instruction count", "block counts",
    };

    [Theory]
    [MemberData(nameof(Differences))]
    public void ARestoredUnitThatDiffersFromEmissionFailsTheCompile(string difference)
    {
        var emitted = Emission([0x00, 0x01, 0x0B]);
        var restored = difference switch
        {
            "name" => Emission([0x00, 0x01, 0x0B], function => function with { Name = "Other" }),
            "type" => Emission([0x00, 0x01, 0x0B], function => function with { Type = WasmFunctionType.Create(CliValueKind.I8) }),
            "body" => Emission([0x00, 0x00, 0x0B]),
            "inlining" => Emission([0x00, 0x01, 0x0B], function => function with { IsNoInlining = true }),
            "environment key" => Emission([0x00, 0x01, 0x0B], environment: "env"),
            "instruction count" => Emission([0x00, 0x01, 0x0B], instructions: 4),
            _ => Emission([0x00, 0x01, 0x0B], blocks: 2),
        };

        var failure = Assert.Throws<InvalidOperationException>(() =>
            NetWasm.Compiler.Wasm.Emission.Methods.ManagedBodyBatchEmitter.Verified("Program::Run", restored, emitted));

        Assert.Contains($"restored a {difference} that differs from emission", failure.Message, StringComparison.Ordinal);
        Assert.Equal(difference == "body", failure.Message.Contains("at byte 1", StringComparison.Ordinal));
    }

    [Fact]
    public void AReplayedBodyThatDiffersFromEmissionFailsTheCompile()
    {
        NetWasm.Compiler.Wasm.Emission.Methods.ManagedBodyBatchEmitter.RequireReplayedBody("Program::Run", [0x00, 0x0B], [0x00, 0x0B]);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            NetWasm.Compiler.Wasm.Emission.Methods.ManagedBodyBatchEmitter.RequireReplayedBody("Program::Run", [0x00, 0x0B], [0x00, 0x01]));

        Assert.Contains("differs from emission at byte 1", failure.Message, StringComparison.Ordinal);
    }
}
