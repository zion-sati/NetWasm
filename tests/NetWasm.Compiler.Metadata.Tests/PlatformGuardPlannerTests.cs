// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        Assert.Equal(0, result.FalseCalls[0]);
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
    // a branch with nothing to test or nothing after it (back to the call), the method's end, and any other instruction.
    [Theory]
    [InlineData(new byte[] { 0x0a, 0x0b, 0x2a })]
    [InlineData(new byte[] { 0x0a, 0x07, 0x2a })]
    [InlineData(new byte[] { 0xfe, 0x01, 0x2a })]
    [InlineData(new byte[] { 0x0a, 0x2d, 0, 0x2a })]
    [InlineData(new byte[] { 0x2d, 0xf9 })]
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

    public static TheoryData<string, int> OtherOperatingSystems => new()
    {
        { "IsWindows", 0 }, { "IsWindowsVersionAtLeast", 4 }, { "IsLinux", 0 }, { "IsFreeBSD", 0 },
        { "IsFreeBSDVersionAtLeast", 4 }, { "IsAndroid", 0 }, { "IsAndroidVersionAtLeast", 4 }, { "IsIOS", 0 },
        { "IsIOSVersionAtLeast", 3 }, { "IsMacOS", 0 }, { "IsMacOSVersionAtLeast", 3 }, { "IsMacCatalyst", 0 },
        { "IsMacCatalystVersionAtLeast", 3 }, { "IsTvOS", 0 }, { "IsTvOSVersionAtLeast", 3 }, { "IsWatchOS", 0 },
        { "IsWatchOSVersionAtLeast", 3 },
    };

    // OperatingSystem's check for any operating system but the browser and WASI is false whatever its arguments,
    // which the decoder pops: the true arm is gone.
    [Theory]
    [MemberData(nameof(OtherOperatingSystems))]
    public void EveryOtherOperatingSystemIsFalse(string check, int arguments)
    {
        using var image = new PEReader(new MemoryStream(PlatformImage("NetWasm.CoreLib",
            new PlatformMember("System", "OperatingSystem", check, Int32Parameters: arguments))));
        byte[] bytes = [.. Enumerable.Repeat((byte)0x16, arguments), 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        var plan = Planner().Plan(Snapshot(image.GetMetadataReader()), bytes, []);

        Assert.NotNull(plan);
        Assert.Equal(arguments, plan.FalseCalls[arguments]);
        Assert.Equal(arguments + 7, plan.BranchTargets[arguments + 5]);
        Assert.DoesNotContain(arguments + 10, plan.LiveOffsets);
    }

    // The browser and WASI are decided when the program runs; a check of another shape, or another assembly's
    // OperatingSystem, proves nothing.
    [Theory]
    [InlineData("NetWasm.CoreLib", "IsBrowser", 0, true)]
    [InlineData("NetWasm.CoreLib", "IsWasi", 0, true)]
    [InlineData("NetWasm.CoreLib", "IsOSPlatformVersionAtLeast", 4, true)]
    [InlineData("NetWasm.CoreLib", "IsWindows", 0, false)]
    [InlineData("UserLibrary", "IsWindows", 0, true)]
    public void RunTimePlatformsAndOtherShapesProveNothing(string owner, string check, int arguments, bool returnsBool)
    {
        using var image = new PEReader(new MemoryStream(PlatformImage(owner,
            new PlatformMember("System", "OperatingSystem", check, Int32Parameters: arguments, ReturnsBool: returnsBool))));
        byte[] bytes = [.. Enumerable.Repeat((byte)0x16, arguments), 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        Assert.Null(Planner().Plan(Snapshot(image.GetMetadataReader()), bytes, []));
    }

    // OperatingSystem.IsOSPlatform of a literal name loaded just before it is false for any name but WASI's and the
    // browser's, case-insensitively; the decoder pops the name.
    [Theory]
    [InlineData("windows", true)]
    [InlineData("Linux", true)]
    [InlineData("WASI", false)]
    [InlineData("browser", false)]
    public void PlatformNamesOtherThanWasiAndTheBrowserAreFalse(string platform, bool proven)
    {
        using var image = new PEReader(new MemoryStream(PlatformImageWithStrings("NetWasm.CoreLib",
            [new("System", "OperatingSystem", "IsOSPlatform", StringParameter: true)], [platform])));
        byte[] bytes = [0x72, 1, 0, 0, 0x70, 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        var plan = Planner().Plan(Snapshot(image.GetMetadataReader()), bytes, []);

        Assert.Equal(proven, plan is not null);
        if (plan is not null) Assert.Equal(1, plan.FalseCalls[5]);
    }

    // A name the test does not load just before it, or one a branch can replace, proves nothing.
    [Fact]
    public void APlatformNameMustBeLoadedJustBeforeTheTest()
    {
        using var image = new PEReader(new MemoryStream(PlatformImageWithStrings("NetWasm.CoreLib",
            [new("System", "OperatingSystem", "IsOSPlatform", StringParameter: true)], ["windows"])));
        var source = Snapshot(image.GetMetadataReader());
        byte[] separated = [0x72, 1, 0, 0, 0x70, 0x00, 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        // A branch reaches the test with another name (here the same literal, which the proof cannot know).
        byte[] entered = [0x72, 1, 0, 0, 0x70, 0x2b, 5, 0x72, 1, 0, 0, 0x70, 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42,
            0x2a, 0x16, 0x2a];
        byte[] first = [0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        byte[] notString = [0x14, 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        // An ldstr whose token names no user string, or a string past the heap, which the decoder rejects.
        byte[] notUserString = [0x72, 1, 0, 0, 0x0a, 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];
        byte[] pastTheHeap = [0x72, 0xf0, 0xff, 0xff, 0x70, 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        Assert.Null(Planner().Plan(source, notUserString, []));
        Assert.Null(Planner().Plan(source, pastTheHeap, []));
        Assert.Null(Planner().Plan(source, separated, []));
        Assert.Null(Planner().Plan(source, entered, []));
        Assert.Null(Planner().Plan(source, first, []));
        Assert.Null(Planner().Plan(source, notString, []));
    }

    // RuntimeInformation.IsOSPlatform of OSPlatform's Windows, Linux, OSX or FreeBSD property, read just before it, is
    // false; another property, or one of another type, proves nothing.
    [Theory]
    [InlineData("get_Windows", "OSPlatform", true)]
    [InlineData("get_Linux", "OSPlatform", true)]
    [InlineData("get_OSX", "OSPlatform", true)]
    [InlineData("get_FreeBSD", "OSPlatform", true)]
    [InlineData("get_Browser", "OSPlatform", false)]
    [InlineData("get_Windows", "OtherPlatform", false)]
    public void OtherOSPlatformPropertiesAreFalse(string property, string type, bool proven)
    {
        using var image = new PEReader(new MemoryStream(PlatformImage("NetWasm.CoreLib",
            new PlatformMember("System.Runtime.InteropServices", "RuntimeInformation", "IsOSPlatform", OSPlatformParameter: true),
            new PlatformMember("System.Runtime.InteropServices", type, property, ReturnsOSPlatform: true))));
        byte[] bytes = [0x28, 2, 0, 0, 0x0a, 0x28, 1, 0, 0, 0x0a, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        var plan = Planner().Plan(Snapshot(image.GetMetadataReader()), bytes, []);

        Assert.Equal(proven, plan is not null);
        if (plan is not null) Assert.Equal(1, plan.FalseCalls[5]);
    }

    // Debug IL joins `IsWindows() || IsLinux()` before testing it: once the first check's true arm is proven dead, the
    // join has only the second check's way in, so a later round proves the test and the body is gone.
    [Fact]
    public void ALaterRoundProvesATestAJoinOnlyTheProofReaches()
    {
        using var image = new PEReader(new MemoryStream(PlatformImage("NetWasm.CoreLib",
            new PlatformMember("System", "OperatingSystem", "IsWindows"))));
        byte[] bytes = [0x28, 1, 0, 0, 0x0a, 0x2d, 7, 0x28, 1, 0, 0, 0x0a, 0x2b, 1, 0x17, 0x2d, 3, 0x1f, 42, 0x2a, 0x16,
            0x2a];

        var plan = Planner().Plan(Snapshot(image.GetMetadataReader()), bytes, []);

        Assert.NotNull(plan);
        Assert.Equal(7, plan.BranchTargets[5]);
        Assert.Equal(17, plan.BranchTargets[15]);
        Assert.DoesNotContain(14, plan.LiveOffsets);
        Assert.DoesNotContain(20, plan.LiveOffsets);
    }

    // A join another live path also reaches may bring a true value, so a proof stops there.
    [Fact]
    public void AJoinAnotherLivePathReachesProvesNothing()
    {
        using var image = new PEReader(new MemoryStream(PlatformImage("NetWasm.CoreLib",
            new PlatformMember("System", "OperatingSystem", "IsWindows"))));
        byte[] bytes = [0x02, 0x2d, 7, 0x28, 1, 0, 0, 0x0a, 0x2b, 1, 0x17, 0x2d, 3, 0x1f, 42, 0x2a, 0x16, 0x2a];

        Assert.Null(Planner().Plan(Snapshot(image.GetMetadataReader()), bytes, []));
    }

    private sealed record PlatformMember(string Namespace, string Type, string Name, int Int32Parameters = 0,
        bool StringParameter = false, bool OSPlatformParameter = false, bool ReturnsOSPlatform = false,
        bool ReturnsBool = true);

    private static byte[] PlatformImage(string owner, params PlatformMember[] members) =>
        PlatformImageWithStrings(owner, members, []);

    // Member references 0x0a000001 onwards, in order, and user strings in order from 0x70000001.
    private static byte[] PlatformImageWithStrings(string owner, PlatformMember[] members, string[] strings)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Consumer.dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Consumer"), new(1, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var assembly = metadata.AddAssemblyReference(metadata.GetOrAddString(owner), new(1, 0), default, default, 0, default);
        var platform = metadata.AddTypeReference(assembly, metadata.GetOrAddString("System.Runtime.InteropServices"),
            metadata.GetOrAddString("OSPlatform"));
        foreach (var member in members)
        {
            var type = metadata.AddTypeReference(assembly, metadata.GetOrAddString(member.Namespace),
                metadata.GetOrAddString(member.Type));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature()
                .Parameters(member.Int32Parameters + (member.StringParameter || member.OSPlatformParameter ? 1 : 0),
                    result =>
                    {
                        if (member.ReturnsOSPlatform) result.Type().Type(platform, isValueType: true);
                        else if (member.ReturnsBool) result.Type().Boolean();
                        else result.Type().Int32();
                    },
                    parameters =>
                    {
                        if (member.StringParameter) parameters.AddParameter().Type().String();
                        if (member.OSPlatformParameter) parameters.AddParameter().Type().Type(platform, isValueType: true);
                        for (var index = 0; index < member.Int32Parameters; index++) parameters.AddParameter().Type().Int32();
                    });
            metadata.AddMemberReference(type, metadata.GetOrAddString(member.Name), metadata.GetOrAddBlob(signature));
        }
        foreach (var value in strings) metadata.GetOrAddUserString(value);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
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
