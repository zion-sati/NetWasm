using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests.UnsafeAccessors;

public sealed class UnsafeAccessorFieldExposureClassifierTests
{
    private static readonly AssemblyIdentity Assembly = new("Fields");
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "Test", "Holder", false);
    private static readonly CliTypeIdentity SystemType = CliTypeIdentity.Named(new("Core"), "System", "Type", false);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);

    [Theory]
    [InlineData("instance")]
    [InlineData("static")]
    [InlineData("omitted-name")]
    [InlineData("byref-owner")]
    [InlineData("open-generic-owner")]
    [InlineData("different-closed-owner")]
    [InlineData("modified-types")]
    [InlineData("external-declaration")]
    [InlineData("translated-owner")]
    public void PossibleAddressExposureInvalidatesTheDefinitionProof(string shape)
    {
        var method = Method();
        var field = Field();
        switch (shape)
        {
            case "static":
                method = method with { UnsafeAccessor = method.UnsafeAccessor! with { Kind = 4 } };
                field = field with { Definition = field.Definition with { IsStatic = true } };
                break;
            case "omitted-name":
                method = method with { Name = "_type", UnsafeAccessor = method.UnsafeAccessor! with { Name = null, NameSpecified = false } };
                break;
            case "byref-owner":
                method = WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType), CliTypeIdentity.ManagedByReference(Owner));
                break;
            case "open-generic-owner":
                method = WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType),
                    CliTypeIdentity.GenericInstantiation(Owner, [CliTypeIdentity.GenericParameter(false, 0)]));
                field = field with { DeclaringType = CliTypeIdentity.GenericInstantiation(Owner, [Integer]) };
                break;
            case "different-closed-owner":
                method = WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType), CliTypeIdentity.GenericInstantiation(Owner, [SystemType]));
                field = field with { DeclaringType = CliTypeIdentity.GenericInstantiation(Owner, [Integer]) };
                break;
            case "modified-types":
                method = WithSignature(method, Modified(CliTypeIdentity.ManagedByReference(Modified(SystemType))), Modified(Owner));
                field = field with { Definition = field.Definition with { SignatureType = Modified(SystemType) } };
                break;
            case "external-declaration":
                method = method with { Key = new(new("External"), 0x06000001), DeclaringType = new(new("External"), 0x02000001) };
                break;
            case "translated-owner":
                method = WithSignature(method, CliTypeIdentity.ManagedByReference(Integer), Integer) with
                { UnsafeAccessor = method.UnsafeAccessor! with { TypeTranslations = new(["Fixture.Owner, Fixture"]) } };
                break;
        }
        Assert.True(Create().Classify(method, field));
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("physical-body")]
    [InlineData("nonstatic")]
    [InlineData("malformed")]
    [InlineData("method-kind")]
    [InlineData("invalid-kind")]
    [InlineData("wrong-staticness")]
    [InlineData("other-name")]
    [InlineData("omitted-other-name")]
    [InlineData("explicit-null")]
    [InlineData("explicit-empty")]
    [InlineData("no-owner")]
    [InlineData("extra-parameter")]
    [InlineData("byvalue-result")]
    [InlineData("different-result")]
    [InlineData("different-owner")]
    [InlineData("different-assembly")]
    [InlineData("generic-parameter-owner")]
    public void UnrelatedOrInvalidDeclarationsDoNotExposeTheField(string shape)
    {
        var method = Method();
        method = shape switch
        {
            "ordinary" => method with { UnsafeAccessor = null },
            "physical-body" => method with { RelativeVirtualAddress = 1 },
            "nonstatic" => method with { IsStatic = false },
            "malformed" => method with { UnsafeAccessor = method.UnsafeAccessor! with { IsMalformed = true } },
            "method-kind" => method with { UnsafeAccessor = method.UnsafeAccessor! with { Kind = 1 } },
            "invalid-kind" => method with { UnsafeAccessor = method.UnsafeAccessor! with { Kind = 99 } },
            "wrong-staticness" => method with { UnsafeAccessor = method.UnsafeAccessor! with { Kind = 4 } },
            "other-name" => method with { UnsafeAccessor = method.UnsafeAccessor! with { Name = "other" } },
            "omitted-other-name" => method with { UnsafeAccessor = method.UnsafeAccessor! with { NameSpecified = false } },
            "explicit-null" => method with { Name = "_type", UnsafeAccessor = method.UnsafeAccessor! with { Name = null } },
            "explicit-empty" => method with { Name = "_type", UnsafeAccessor = method.UnsafeAccessor! with { Name = "" } },
            "no-owner" => WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType)),
            "extra-parameter" => WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType), Owner, Integer),
            "byvalue-result" => WithSignature(method, SystemType, Owner),
            "different-result" => WithSignature(method, CliTypeIdentity.ManagedByReference(Integer), Owner),
            "different-owner" => WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType), CliTypeIdentity.Named(Assembly, "Test", "Other", false)),
            "different-assembly" => WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType), CliTypeIdentity.Named(new("Other"), "Test", "Holder", false)),
            "generic-parameter-owner" => WithSignature(method, CliTypeIdentity.ManagedByReference(SystemType), CliTypeIdentity.GenericParameter(false, 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        Assert.False(Create().Classify(method, Field()));
    }

    [Fact]
    public void MissingInputsFailAtTheBoundary()
    {
        var classifier = Create();
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!, Field()));
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(Method(), null!));
    }

    private static IUnsafeAccessorFieldExposureClassifier Create() => ThroughContract(new UnsafeAccessorFieldExposureClassifier());
    private static IUnsafeAccessorFieldExposureClassifier ThroughContract(IUnsafeAccessorFieldExposureClassifier classifier) => classifier;
    private static CliTypeIdentity Modified(CliTypeIdentity type) => CliTypeIdentity.Modified(type, Integer, true);
    private static MethodDefinitionModel WithSignature(MethodDefinitionModel method, CliTypeIdentity result, params CliTypeIdentity[] parameters) =>
        method with { Signature = new(result, [.. parameters]) };
    private static MethodDefinitionModel Method() => new(new(Assembly, 0x06000001), new(Assembly, 0x02000002),
        "Address", true, new(CliTypeIdentity.ManagedByReference(SystemType), [Owner]), 0)
    { UnsafeAccessor = new((int)UnsafeAccessorMemberKind.Field, "_type", true, false) };
    private static FieldInstanceModel Field() => new(new(new(Assembly, 0x04000001), new(Assembly, 0x02000001),
        "_type", SystemType, false)
    { IsInitOnly = true }, Owner, SystemType);
}
