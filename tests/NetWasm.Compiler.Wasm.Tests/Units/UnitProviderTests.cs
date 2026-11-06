using System.Collections;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
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
    public void RecordedTypeObjectAddressesRecordEachTypeLookedUp()
    {
        var log = new UnitDependencyLog();
        var layout = new RecordingStaticDataLayout(new Data(), log);
        var addresses = layout.TypeObjectAddresses;

        log.Begin();
        Assert.True(addresses.TryGetValue("Program::Point", out var address));
        Assert.False(addresses.TryGetValue("Program::Missing", out _));
        var lookups = log.End();

        Assert.Equal(128, address);
        Assert.Same(addresses, layout.TypeObjectAddresses);
        Assert.Equal(["Program::Missing", "Program::Point"], lookups.Select(dependency => dependency.Key).Order());
        Assert.All(lookups, dependency => Assert.Equal(UnitQueryKind.TypeObjectAddress, dependency.Kind));
    }

    [Fact]
    public void ARecordedLookupRecordsEachKeyAndWhatItFound()
    {
        var log = new UnitDependencyLog();
        var lookup = new RecordingLookup<string, int>(Lookups.Of(new Dictionary<string, int> { ["a"] = 1 }), log,
            UnitQueryKind.ByRefEffects, key => key, static (ref answer, value) => answer.Add(value));

        log.Begin();
        Assert.True(lookup.TryGetValue("a", out var found));
        Assert.False(lookup.TryGetValue("b", out _));
        var lookups = log.End().ToDictionary(dependency => dependency.Key);

        Assert.Equal(1, found);
        Assert.Equal(new UnitLookup<int>(true, 1), lookups["a"].Value);
        Assert.Equal(new UnitLookup<int>(false, 0), lookups["b"].Value);
        Assert.NotEqual(lookups["a"].Answer, lookups["b"].Answer);
    }

    [Fact]
    public void ARecordedMembershipRecordsEachItemAndItsAnswer()
    {
        var log = new UnitDependencyLog();
        var membership = new RecordingMembership<string>(Lookups.Of(new HashSet<string> { "Program::Point" }), log,
            UnitQueryKind.AllocatingConstructedMethod, item => item);

        log.Begin();
        Assert.True(membership.Contains("Program::Point"));
        Assert.False(membership.Contains("Program::Other"));
        var tests = log.End().ToDictionary(dependency => dependency.Key);

        Assert.Equal(["Program::Other", "Program::Point"], tests.Keys.Order());
        Assert.Equal(true, tests["Program::Point"].Value);
        Assert.Equal(false, tests["Program::Other"].Value);
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
        Assert.True(data.TypeObjectAddresses.TryGetValue("Program::Point", out var point));
        Assert.Equal(128, point);
        Assert.False(data.TypeObjectAddresses.TryGetValue("Program::Missing", out _));
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
        Assert.Throws<UnitReplayMissException>(() => data.TypeObjectAddresses.TryGetValue("Program::Point", out _));
    }

    [Fact]
    public void AReplayedMembershipAnswersAsTheLastCompileDid()
    {
        var membership = new ReplayMembership<string>(
            Answers(((UnitQueryKind.Finalizable, "Program::Point"), true), ((UnitQueryKind.Finalizable, "Program::Other"), false)),
            UnitQueryKind.Finalizable, item => item);

        Assert.True(membership.Contains("Program::Point"));
        Assert.False(membership.Contains("Program::Other"));
        Assert.Throws<UnitReplayMissException>(() => membership.Contains("Program::Unasked"));
    }

    [Fact]
    public void ReplayedTargetMapsAnswerByKeyByMemberOrWhole()
    {
        IReadOnlyList<string> delegates = ["Func"];
        var answers = Answers(
            ((UnitQueryKind.DispatchCallSite, "site"), new UnitLookup<int>(true, 5)),
            ((UnitQueryKind.DispatchCallSite, "absent"), new UnitLookup<int>(false, 0)),
            ((UnitQueryKind.Finalizable, "type"), true),
            ((UnitQueryKind.DelegateTypes, ""), delegates));
        var maps = Assert.IsAssignableFrom<IUnitTargetMaps>(new ReplayTargetMaps(answers));
        var keyed = maps.Answer(Lookups.NoEntries<string, int>(), UnitQueryKind.DispatchCallSite, key => key,
            static (ref answer, value) => answer.Add(value));
        var membership = maps.Answer(Lookups.NoItems<string>(), UnitQueryKind.Finalizable, item => item);
        var whole = maps.Answer(Lookups.Whole<IReadOnlyList<string>>([]), UnitQueryKind.DelegateTypes,
            static _ => default(UnitFingerprint));

        Assert.True(keyed.TryGetValue("site", out var site));
        Assert.Equal(5, site);
        Assert.False(keyed.TryGetValue("absent", out _));
        Assert.True(membership.Contains("type"));
        Assert.Same(delegates, whole.Read());
        Assert.Throws<UnitReplayMissException>(() => keyed.TryGetValue("unasked", out _));
        Assert.Throws<UnitReplayMissException>(() => maps.Answer(Lookups.Whole(ImmutableArray<int>.Empty),
            UnitQueryKind.CallableMethods, static _ => default(UnitFingerprint)).Read());
    }

    [Fact]
    public void RecordedTargetMapsRecordEachLookupTestAndRead()
    {
        var log = new UnitDependencyLog();
        var maps = Assert.IsAssignableFrom<IUnitTargetMaps>(new RecordingTargetMaps(log));
        IReadOnlyList<string> delegates = ["Func"];
        var keyed = maps.Answer(Lookups.Of(new Dictionary<string, int> { ["site"] = 5 }), UnitQueryKind.DispatchCallSite,
            key => key, static (ref answer, value) => answer.Add(value));
        var membership = maps.Answer(Lookups.Of(new HashSet<string> { "type" }), UnitQueryKind.Finalizable, item => item);
        var whole = maps.Answer(Lookups.Whole(delegates), UnitQueryKind.DelegateTypes, static _ => new(2, 2));

        log.Begin();
        Assert.True(keyed.TryGetValue("site", out _));
        Assert.True(membership.Contains("type"));
        Assert.Same(delegates, whole.Read());
        var recorded = log.End();

        Assert.Equal([UnitQueryKind.DispatchCallSite, UnitQueryKind.Finalizable, UnitQueryKind.DelegateTypes],
            recorded.Select(dependency => dependency.Kind));
        Assert.Equal(new UnitFingerprint(2, 2), recorded[2].Answer);
        Assert.Same(delegates, recorded[2].Value);
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
        public IEntryLookup<string, int> TypeObjectAddresses =>
            Lookups.Of(new Dictionary<string, int> { ["Program::Point"] = 128 });
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
// the program's; the method's shadow the program's.
public sealed class UnitQueryArgumentsTests
{
    private static readonly CliTypeIdentity Own = CliTypeIdentity.Named(new AssemblyIdentity("Program"), "", "Own", false);
    private static readonly CliTypeIdentity Shared = CliTypeIdentity.Named(new AssemblyIdentity("Program"), "", "Shared", false);

    [Fact]
    public void AMethodsIdentitiesShadowTheProgramsWhenLookedUp()
    {
        var method = new UnitQueryArguments(Lookups.Of(new Dictionary<string, CliTypeIdentity> { ["T"] = Own }),
            Lookups.Of(new Dictionary<string, FieldInstanceModel>()), Lookups.Of(new Dictionary<string, MethodInstanceModel>()))
        {
            Definitions = [new(new AssemblyIdentity("Program"), 0x04000001)],
        };
        var program = new UnitQueryArguments(
            Lookups.Of(new Dictionary<string, CliTypeIdentity> { ["T"] = Shared, ["U"] = Shared }),
            Lookups.Of(new Dictionary<string, FieldInstanceModel>()), Lookups.Of(new Dictionary<string, MethodInstanceModel>()));

        var types = method.Before(program).Types;

        Assert.True(types.TryGetValue("T", out var own));
        Assert.Same(Own, own);
        Assert.True(types.TryGetValue("U", out var shared));
        Assert.Same(Shared, shared);
        Assert.False(types.TryGetValue("V", out _));
        Assert.Equal(method.Definitions, method.Before(program).Definitions);
    }
}

// The target views a unit's emission reads: a whole read records one query, and function
// indices pass through.
public sealed class UnitTargetViewTests
{
    [Fact]
    public void ARecordedTargetRecordsEachPlanEntryWithWhatItHolds()
    {
        var method = new EntityKey(new AssemblyIdentity("Program"), 0x06000001);
        var group = new StructuredExceptionGroupKey(method, null, new(1));
        var log = new UnitDependencyLog();
        var target = EmitterTestSupport.CreateInstructionModuleTarget() with
        {
            StaticInitializerGuards = Lookups.Of(new Dictionary<string, StaticInitializerGuard>
            {
                ["direct"] = new(240, method, null),
                ["constructed"] = new(240, null, "constructed"),
            }),
            ExceptionMetadata = Lookups.Of(new Dictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>
            {
                [group] = new(512, 2, HasFilters: true),
            }),
            StackTraceLookups = StackTraceMethodLookups.Of(StackTraceMethodPlan.Disabled with
            {
                LocationSymbols = ImmutableDictionary<int, ImmutableArray<StackTraceLocationSymbol>>.Empty
                    .Add(9, [new(0, 100), new(20, 101)]),
            }),
        };

        log.Begin();
        var view = UnitTargetViews.Recording(target, log);
        Assert.True(view.StaticInitializerGuards.TryGetValue("direct", out _));
        Assert.True(view.StaticInitializerGuards.TryGetValue("constructed", out _));
        Assert.True(view.ExceptionMetadata.TryGetValue(group, out var metadata));
        Assert.True(view.StackTraceLookups.LocationSymbols.TryGetValue(9, out var symbols));
        var recorded = log.End().ToDictionary(dependency => (dependency.Kind, dependency.Key));

        Assert.Equal(512, metadata.Address);
        Assert.Equal(2, symbols.Length);
        // A guard's answer names whether it calls the definition or a constructed owner.
        Assert.NotEqual(recorded[(UnitQueryKind.StaticInitializerGuard, "direct")].Answer,
            recorded[(UnitQueryKind.StaticInitializerGuard, "constructed")].Answer);
        Assert.Contains((UnitQueryKind.ExceptionMetadata, UnitTargetViews.ExceptionGroupKeyText(group)), recorded.Keys);
        Assert.Contains((UnitQueryKind.StackTraceLocationSymbols, "9"), recorded.Keys);
    }

    [Fact]
    public void AWholeReadRecordsItsReadAndAnswersAsItsSource()
    {
        var log = new UnitDependencyLog();
        IReadOnlyDictionary<string, int> methods = new Dictionary<string, int> { ["m"] = 3 };
        var read = new RecordingWholeRead<IReadOnlyDictionary<string, int>>(Lookups.Whole(methods), log,
            UnitQueryKind.CallableMethods, static map => new((ulong)map.Count, 1));

        log.Begin();
        Assert.Same(methods, read.Read());
        Assert.Same(methods, read.Read());
        var recorded = log.End();

        // The log keeps a query once however often the method asks it.
        Assert.Equal(new UnitFingerprint(1, 1), Assert.Single(recorded).Answer);
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
