using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class PlatformGuardPlannerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvesDirectAndDebugLocalBooleanBranches(bool throughLocal)
    {
        using var image = new PEReader(new MemoryStream(Image("NetWasm.CoreLib")));
        var source = Snapshot(image.GetMetadataReader());
        byte[] bytes = throughLocal
            ? [0x28, 1, 0, 0, 0x0a, 0x0a, 0x06, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a]
            : [0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        var planner = Assert.IsAssignableFrom<IPlatformGuardPlanner>(new PlatformGuardPlanner(new RawCilInstructionReader()));

        var result = planner.Plan(source, bytes, []);

        Assert.NotNull(result);
        var branch = throughLocal ? 7 : 5;
        Assert.Equal(branch + 2, result.BranchTargets[branch]);
        Assert.Contains(0, result.FalseCalls);
        Assert.DoesNotContain(branch + 5, result.LiveOffsets);
    }

    [Fact]
    public void ProvesBooleanNegation()
    {
        using var image = new PEReader(new MemoryStream(Image("NetWasm.CoreLib")));
        var planner = Assert.IsAssignableFrom<IPlatformGuardPlanner>(new PlatformGuardPlanner(new RawCilInstructionReader()));
        byte[] bytes = [0x28, 1, 0, 0, 0x0a, 0x16, 0xfe, 0x01, 0x2c, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        var result = planner.Plan(Snapshot(image.GetMetadataReader()), bytes, []);
        Assert.NotNull(result);
        Assert.Equal(10, result.BranchTargets[8]);
        Assert.DoesNotContain(13, result.LiveOffsets);
    }

    [Fact]
    public void DoesNotTrustSameNamedUserTypesOrAlternateEntries()
    {
        var planner = Assert.IsAssignableFrom<IPlatformGuardPlanner>(new PlatformGuardPlanner(new RawCilInstructionReader()));
        using var other = new PEReader(new MemoryStream(Image("UserLibrary")));
        byte[] direct = [0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        Assert.Null(planner.Plan(Snapshot(other.GetMetadataReader()), direct, []));
        using var core = new PEReader(new MemoryStream(Image("NetWasm.CoreLib")));
        // The branch enters after the capability value was stored; it cannot
        // inherit the proof from the skipped call.
        byte[] alternate = [0x2b, 6, 0x28, 1, 0, 0, 0x0a, 0x0a, 0x06, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        Assert.Null(planner.Plan(Snapshot(core.GetMetadataReader()), alternate, []));
    }

    [Fact]
    public void UnknownWorkAndInvalidCilRemainForNormalValidation()
    {
        using var image = new PEReader(new MemoryStream(Image("NetWasm.CoreLib")));
        var source = Snapshot(image.GetMetadataReader());
        var planner = Assert.IsAssignableFrom<IPlatformGuardPlanner>(new PlatformGuardPlanner(new RawCilInstructionReader()));
        Assert.Null(planner.Plan(source, [], []));
        Assert.Null(planner.Plan(source, [0xfe], []));
        Assert.Null(planner.Plan(source, [0x28, 1, 0, 0, 0x0a, 0x02, 0x2a], []));
        Assert.Null(planner.Plan(source, [0x28, 1, 0, 0, 0x0a, 0x0a, 0x0b, 0x2a], []));
        // Malformed, nil and out-of-range call tokens are not capability proofs.
        Assert.Null(planner.Plan(source, [0x28, 0xff, 0xff, 0xff, 0xff, 0x2a], []));
        Assert.Null(planner.Plan(source, [0x28, 0, 0, 0, 0x0a, 0x2a], []));
        Assert.Null(planner.Plan(source, [0x28, 0xff, 0xff, 0xff, 0x0a, 0x2a], []));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(source, null!, []));
        Assert.Throws<ArgumentNullException>(() => new PlatformGuardPlanner(null!));
    }

    // Every capability NetWasm lacks is proven false, through short and long locals and branches.
    [Theory]
    [InlineData("get_IsDynamicCodeSupported", new byte[] { 0x13, 4, 0x11, 4, 0x3a, 3, 0, 0, 0 })]
    [InlineData("get_IsDynamicCodeCompiled", new byte[] { 0xfe, 0x0e, 5, 0, 0xfe, 0x0c, 5, 0, 0x39, 3, 0, 0, 0 })]
    [InlineData("get_IsMultithreadingSupported", new byte[] { 0x00, 0x17, 0xfe, 0x01, 0x3a, 3, 0, 0, 0 })]
    public void ProvesEveryCapabilityThroughEveryLocalAndBranchForm(string member, byte[] test)
    {
        using var image = new PEReader(new MemoryStream(Image("NetWasm.CoreLib", member)));
        byte[] bytes = [0x28, 1, 0, 0, 0x0a, .. test, 0x1f, 42, 0x2a, 0x16, 0x2a];
        var branch = 5 + test.Length - 5;

        var result = Planner().Plan(Snapshot(image.GetMetadataReader()), bytes, []);

        Assert.NotNull(result);
        Assert.True(result.BranchTargets.ContainsKey(branch));
    }

    // A proof stops at what it cannot follow: a store or test with too little on the stack, an unknown local,
    // a branch with nothing to test or nothing after it, the method's end, and any other instruction.
    [Theory]
    [InlineData(new byte[] { 0x0a, 0x0b, 0x2a })]
    [InlineData(new byte[] { 0x0a, 0x07, 0x2a })]
    [InlineData(new byte[] { 0xfe, 0x01, 0x2a })]
    [InlineData(new byte[] { 0x0a, 0x2d, 0 })]
    [InlineData(new byte[] { 0x2d, 0xfe })]
    [InlineData(new byte[] { 0x00 })]
    public void AProofStopsAtWhatItCannotFollow(byte[] test)
    {
        using var image = new PEReader(new MemoryStream(Image("NetWasm.CoreLib")));

        Assert.Null(Planner().Plan(Snapshot(image.GetMetadataReader()), [0x28, 1, 0, 0, 0x0a, .. test], []));
    }

    // Only RuntimeFeature's static, parameterless bool getters, by member reference, are capabilities.
    [Theory]
    [InlineData("get_IsSupported", "RuntimeFeature", 0)]
    [InlineData("get_IsDynamicCodeSupported", "OtherFeature", 0)]
    [InlineData("get_IsDynamicCodeSupported", "RuntimeFeature", 1)]
    [InlineData("get_IsDynamicCodeSupported", "RuntimeFeature", 2)]
    [InlineData("get_IsDynamicCodeSupported", "RuntimeFeature", 3)]
    [InlineData("get_IsDynamicCodeSupported", "RuntimeFeature", 4)]
    [InlineData("get_IsDynamicCodeSupported", "RuntimeFeature", 5)]
    [InlineData("get_IsDynamicCodeSupported", "RuntimeFeature", 6)]
    public void OnlyRuntimeFeaturesBooleanGettersAreCapabilities(string member, string type, int shape)
    {
        using var image = new PEReader(new MemoryStream(Image("NetWasm.CoreLib", member, type, shape)));
        byte[] direct = [0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        Assert.Null(Planner().Plan(Snapshot(image.GetMetadataReader()), direct, []));
        Assert.Null(Planner().Plan(Snapshot(image.GetMetadataReader()), [0x28, 1, 0, 0, 0x06, 0x2a], []));
    }

    // A try the live code enters brings its handler and filter; a handler between instructions is not proven.
    [Fact]
    public void LiveTriesBringTheirHandlersAndFilters()
    {
        using var image = new PEReader(new MemoryStream(Image("NetWasm.CoreLib")));
        var source = Snapshot(image.GetMetadataReader());
        byte[] bytes = [0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        var plan = Planner().Plan(source, bytes, [Region(ExceptionRegionKind.Filter, 7, 3, 10, 2, filter: 9)]);
        Assert.NotNull(plan);
        Assert.Contains(10, plan.LiveOffsets);
        Assert.Null(Planner().Plan(source, bytes, [Region(ExceptionRegionKind.Catch, 7, 2, 8, 2)]));
    }

    private static PlatformGuardPlanner Planner() => new(new RawCilInstructionReader());

    private static ExceptionRegion Region(ExceptionRegionKind kind, int tryOffset, int tryLength, int handlerOffset,
        int handlerLength, int filter = 0) =>
        (ExceptionRegion)Assert.Single(typeof(ExceptionRegion).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance))
            .Invoke([kind, tryOffset, tryLength, handlerOffset, handlerLength, kind == ExceptionRegionKind.Filter ? filter : 0]);

    private static MetadataAssemblySnapshot Snapshot(MetadataReader reader) => new(
        new AssemblyIdentity("Consumer"), reader,
        ImmutableDictionary<int, TypeDefinitionModel>.Empty,
        ImmutableDictionary<int, FieldDefinitionModel>.Empty,
        ImmutableDictionary<int, MethodDefinitionModel>.Empty,
        ImmutableDictionary<int, EntityHandle>.Empty);

    // shape: 0 the capability's own; 1 instance; 2 generic; 3 vararg; 4 a parameter; 5 an int result; 6 a member of
    // a type definition rather than a reference.
    private static byte[] Image(string owner, string member = "get_IsDynamicCodeSupported", string typeName = "RuntimeFeature",
        int shape = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Consumer.dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Consumer"), new(1, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var assembly = metadata.AddAssemblyReference(metadata.GetOrAddString(owner), new(1, 0), default, default, 0, default);
        var type = metadata.AddTypeReference(assembly, metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString(typeName));
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(shape == 3 ? SignatureCallingConvention.VarArgs : SignatureCallingConvention.Default,
                genericParameterCount: shape == 2 ? 1 : 0, isInstanceMethod: shape == 1)
            .Parameters(shape == 4 ? 1 : 0,
                result => { if (shape == 5) result.Type().Int32(); else result.Type().Boolean(); },
                parameters => { if (shape == 4) parameters.AddParameter().Type().Int32(); });
        metadata.AddMemberReference(shape == 6 ? MetadataTokens.TypeDefinitionHandle(1) : type,
            metadata.GetOrAddString(member), metadata.GetOrAddBlob(signature));
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
    }
}
