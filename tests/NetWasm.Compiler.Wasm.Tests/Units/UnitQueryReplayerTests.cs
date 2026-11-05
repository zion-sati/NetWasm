using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A restored unit's queries are asked again in the new compile, each rebuilt from its key:
// one that rebuilds is answered as its recorder fingerprints it, one that cannot is
// unbuildable, and one whose provider now fails has failed.
public sealed class UnitQueryReplayerTests
{
    private static readonly AssemblyIdentity Program = new("Program");
    private static readonly CliTypeIdentity Point = CliTypeIdentity.Named(Program, "", "Point", isValueType: true);
    private static readonly CliTypeIdentity Broken = CliTypeIdentity.Named(Program, "", "Broken", isValueType: true);
    private static readonly EntityKey ValueField = new(Program, 0x04000001);
    private static readonly FieldDefinitionModel ValueDefinition = new(ValueField, new(Program, 0x02000002), "Value", Point, false);
    private static readonly FieldInstanceModel Value = new(ValueDefinition, Point, Point);

    private static readonly UnitQueryArguments Arguments = new(
        new Dictionary<string, CliTypeIdentity> { [Point.CanonicalName] = Point, [Broken.CanonicalName] = Broken },
        new Dictionary<string, FieldInstanceModel> { [Value.CanonicalName] = Value },
        new Dictionary<string, MethodInstanceModel>());

    public static TheoryData<string, string> Answered => new()
    {
        { nameof(UnitQueryKind.ObjectLayout), Point.CanonicalName },
        { nameof(UnitQueryKind.ObjectLayoutLookup), Point.CanonicalName },
        { nameof(UnitQueryKind.ObjectLayoutOfDefinition), "Program:0x02000002" },
        { nameof(UnitQueryKind.StringTypeId), "" },
        { nameof(UnitQueryKind.TypeTypeId), "" },
        { nameof(UnitQueryKind.ValueLayout), Point.CanonicalName + "[]" },
        { nameof(UnitQueryKind.ValueLayout), Point.CanonicalName + "[*]" },
        { nameof(UnitQueryKind.ValueLayout), Point.CanonicalName + "[,,]" },
        { nameof(UnitQueryKind.InstanceField), Value.CanonicalName },
        { nameof(UnitQueryKind.InstanceFieldOfDefinition), "Program:0x04000001" },
        { nameof(UnitQueryKind.StaticField), Value.CanonicalName },
        { nameof(UnitQueryKind.StaticFieldOfDefinition), "Program:0x04000001" },
        { nameof(UnitQueryKind.StringLayout), "hello" },
        { nameof(UnitQueryKind.StaticDataEnd), "" },
        { nameof(UnitQueryKind.StaticRootAddresses), "" },
        { nameof(UnitQueryKind.DataSegments), "" },
        { nameof(UnitQueryKind.TypeFactsTable), "address" },
        { nameof(UnitQueryKind.TypeFactsTable), "count" },
        { nameof(UnitQueryKind.TypeObjectAddress), "Program::Point" },
        { nameof(UnitQueryKind.TypeObjectTable), "address" },
        { nameof(UnitQueryKind.TypeObjectTable), "cache" },
        { nameof(UnitQueryKind.TypeObjectTable), "count" },
        { nameof(UnitQueryKind.TypeObjectTable), "size" },
        { nameof(UnitQueryKind.FieldDescriptorAddress), Value.CanonicalName },
        { nameof(UnitQueryKind.MemberDescriptorOffsets), "declaring-type-id" },
        { nameof(UnitQueryKind.MemberDescriptorOffsets), "requires-declaring-type" },
        { nameof(UnitQueryKind.ExceptionObject), nameof(ManagedExceptionKind.Overflow) },
        { nameof(UnitQueryKind.TypeDescriptors), "" },
        { nameof(UnitQueryKind.ConstructedTypeDescriptors), "" },
        { nameof(UnitQueryKind.ValueTypeDescriptors), "" },
        { nameof(UnitQueryKind.MetadataTypeDescriptors), "" },
        { nameof(UnitQueryKind.EnumMetadata), "" },
        { nameof(UnitQueryKind.ManagedCallSite), "caller@0000001f" },
        { nameof(UnitQueryKind.ByRefEffects), "callee" },
        { nameof(UnitQueryKind.DispatchCallSite), "caller@4" },
        { nameof(UnitQueryKind.TypeTestSite), "caller@8" },
        { nameof(UnitQueryKind.RootMap), "callee" },
        { nameof(UnitQueryKind.FunctionAddress), "c:constructed" },
        { nameof(UnitQueryKind.FunctionAddress), "d:Program:0x06000001" },
        { nameof(UnitQueryKind.Finalizable), "7" },
        { nameof(UnitQueryKind.DelegateTypes), "" },
        { nameof(UnitQueryKind.CallableMethods), "" },
        { nameof(UnitQueryKind.TargetPlans), "" },
        { nameof(UnitQueryKind.StackTraceDirectMethodId), "Program:0x06000001" },
        { nameof(UnitQueryKind.StackTraceConstructedMethodId), "constructed" },
        { nameof(UnitQueryKind.StackTraceLocationSymbols), "3" },
        { nameof(UnitQueryKind.StaticInitializerGuard), "Program::Point" },
        { nameof(UnitQueryKind.ExceptionMetadata), "Program:0x06000001\u001F\u001F2" },
    };

    [Theory]
    [MemberData(nameof(Answered))]
    public void AQueryRebuiltFromItsKeyIsAnsweredAsItsRecorderFingerprintsIt(string kind, string key)
    {
        var (replayer, log) = Replayer();

        var answer = replayer.Replay(Enum.Parse<UnitQueryKind>(kind), key, Arguments, EmitterTestSupport.CreateInstructionModuleTarget());

        Assert.Equal(UnitQueryStatus.Answered, answer.Status);
        Assert.NotEqual(default, answer.Fingerprint);
        Assert.Contains(log.Answers, dependency => dependency.Kind == Enum.Parse<UnitQueryKind>(kind) && dependency.Key == key);
    }

    public static TheoryData<string, string> Unbuildable => new()
    {
        { nameof(UnitQueryKind.ObjectLayout), "Program::Missing" },
        { nameof(UnitQueryKind.ValueLayout), "[]" },
        { nameof(UnitQueryKind.ValueLayout), Point.CanonicalName + "[" },
        { nameof(UnitQueryKind.ValueLayout), "Program::Missing[]" },
        { nameof(UnitQueryKind.ValueLayout), Point.CanonicalName + "[x]" },
        { nameof(UnitQueryKind.ObjectLayoutOfDefinition), "Program" },
        { nameof(UnitQueryKind.InstanceFieldOfDefinition), "Program:0xzz" },
        { nameof(UnitQueryKind.StaticFieldOfDefinition), "Program" },
        { nameof(UnitQueryKind.InstanceField), "Program::Missing::0x04000001" },
        { nameof(UnitQueryKind.StaticField), Point.CanonicalName + "::0xzz" },
        { nameof(UnitQueryKind.InstanceField), Point.CanonicalName + "::0x04000009" },
        { nameof(UnitQueryKind.FieldDescriptorAddress), "missing" },
        { nameof(UnitQueryKind.ExceptionObject), "Bogus" },
        { nameof(UnitQueryKind.ManagedCallSite), "caller" },
        { nameof(UnitQueryKind.ManagedCallSite), "caller@zz" },
        { nameof(UnitQueryKind.ManagedCallSite), "caller@1f" },
        { nameof(UnitQueryKind.Finalizable), "seven" },
        { nameof(UnitQueryKind.StackTraceDirectMethodId), "method" },
        { nameof(UnitQueryKind.StackTraceLocationSymbols), "three" },
        { nameof(UnitQueryKind.ExceptionMetadata), "Program:0x06000001" },
        { "999", "" },
    };

    [Theory]
    [MemberData(nameof(Unbuildable))]
    public void AQueryWhoseArgumentCannotBeRebuiltIsUnbuildable(string kind, string key)
    {
        var (replayer, _) = Replayer();

        var answer = replayer.Replay(Enum.Parse<UnitQueryKind>(kind), key, Arguments, EmitterTestSupport.CreateInstructionModuleTarget());

        Assert.Equal(UnitQueryStatus.Unbuildable, answer.Status);
    }

    // A field the arguments do not hold is found through its declaring type's definition;
    // a key that spells its token otherwise than the field's own name is not that field.
    [Fact]
    public void AFieldIsRebuiltThroughItsDeclaringTypeOnlyByItsOwnName()
    {
        var (replayer, _) = Replayer();
        var arguments = Arguments with { Fields = new Dictionary<string, FieldInstanceModel>() };
        var target = EmitterTestSupport.CreateInstructionModuleTarget();

        Assert.Equal(UnitQueryStatus.Answered,
            replayer.Replay(UnitQueryKind.InstanceField, Value.CanonicalName, arguments, target).Status);
        Assert.Equal(UnitQueryStatus.Unbuildable,
            replayer.Replay(UnitQueryKind.InstanceField, Point.CanonicalName + "::0x004000001", arguments, target).Status);
    }

    [Fact]
    public void AFieldTypeTheMethodNamesByDefinitionIsRebuilt()
    {
        var (replayer, _) = Replayer();
        var arguments = new UnitQueryArguments(new Dictionary<string, CliTypeIdentity>(),
            new Dictionary<string, FieldInstanceModel>(), new Dictionary<string, MethodInstanceModel>())
        {
            Definitions = [new(Program, 0x06000001), ValueField],
        };

        Assert.Equal(UnitQueryStatus.Answered, replayer.Replay(UnitQueryKind.ValueLayout, Point.CanonicalName, arguments,
            EmitterTestSupport.CreateInstructionModuleTarget()).Status);
    }

    [Theory]
    [InlineData(nameof(UnitQueryKind.ValueLayout), "Broken")]
    [InlineData(nameof(UnitQueryKind.ObjectLayoutOfDefinition), "Program:0x02000099")]
    public void AQueryWhoseProviderNowFailsHasFailed(string kind, string key)
    {
        var (replayer, _) = Replayer();

        var answer = replayer.Replay(Enum.Parse<UnitQueryKind>(kind), key == "Broken" ? Broken.CanonicalName : key, Arguments,
            EmitterTestSupport.CreateInstructionModuleTarget());

        Assert.Equal(UnitQueryStatus.Failed, answer.Status);
    }

    private static (UnitQueryReplayer Replayer, UnitDependencyLog Log) Replayer()
    {
        var log = new UnitDependencyLog();
        var replayer = new UnitQueryReplayer(log, asking =>
            {
                log.Begin();
                try
                {
                    asking();
                }
                finally
                {
                    _ = log.End();
                }
                return log.Answers;
            },
            new RecordingTypeLayoutProvider(new Layouts(), log), new RecordingValueLayoutProvider(new Values(), log),
            new RecordingInstanceFieldLayoutProvider(new Fields(), log), new RecordingStaticFieldLayoutProvider(new Statics(), log),
            new RecordingStaticDataLayout(new Data(), log), new RecordingMemberDescriptorLayout(new Members(), log),
            new RecordingManagedExceptionObjectProvider(new Exceptions(), log),
            new RecordingTypeDescriptorSource(new Descriptors(), log), new Definitions(), new FieldDefinitions());
        return (replayer, log);
    }

    private sealed class Layouts : ITypeLayoutProvider
    {
        public int StringTypeId => 2;
        public int TypeTypeId => 3;
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => new(9, 16, []);
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            layout = default;
            return false;
        }
        public ObjectLayout GetObjectLayout(EntityKey type) =>
            type.MetadataToken == 0x02000099 ? throw new KeyNotFoundException("no such type") : new(10, 24, [8]);
    }

    private sealed class Values : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) => type == Broken
            ? throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, "broken"))
            : new(type, 8, 4, []);
    }

    private sealed class Fields : IInstanceFieldLayoutProvider
    {
        public FieldLayout GetFieldLayout(FieldInstanceModel field) => new(4);
        public FieldLayout GetFieldLayout(EntityKey field) => new(8);
    }

    private sealed class Statics : IStaticFieldLayoutProvider
    {
        public StaticFieldLayout GetStaticFieldLayout(EntityKey field) => new(64);
        public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) => new(72);
    }

    private sealed class Data : IStaticDataLayout
    {
        public StringLayout GetStringLayout(string value) => new(64, value.Length, 12);
        public int StaticDataEnd => 256;
        public ImmutableArray<int> StaticRootAddresses => [32];
        public ImmutableArray<DataSegment> DataSegments => [new(16, [1, 2])];
        public int TypeFactsTableAddress => 512;
        public int TypeFactsTableCount => 4;
        public IReadOnlyDictionary<string, int> TypeObjectAddresses => new Dictionary<string, int> { ["Program::Point"] = 128 };
        public int TypeObjectTableAddress => 1024;
        public int TypeObjectCacheAddress => 2048;
        public int TypeObjectCount => 3;
        public int TypeObjectSize => 24;
    }

    private sealed class Members : IMemberDescriptorLayout
    {
        public int GetDescriptorAddress(MethodInstanceModel method) => 100;
        public int GetDescriptorAddress(FieldInstanceModel field) => 200;
        public int DeclaringTypeIdOffset => 4;
        public int RequiresDeclaringTypeOffset => 8;
    }

    private sealed class Exceptions : IManagedExceptionObjectProvider
    {
        public int GetExceptionObject(ManagedExceptionKind kind) => 300;
    }

    private sealed class Descriptors : ITypeDescriptorSource, IEnumMetadataSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors => [];
        public ImmutableArray<EnumMetadataLayout> EnumMetadata => [];
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new(Program, 0x02000002), "", "Point", true, [new(Program, 0x04000008), ValueField], []);
    }

    private sealed class FieldDefinitions : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => ValueDefinition;
    }
}
