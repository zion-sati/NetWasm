using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryClassifierTests
{
    private static readonly AssemblyIdentity Assembly = new("Core");
    private static readonly EntityKey InstanceKey = new(Assembly, 1);
    private static readonly EntityKey ExtensionKey = new(Assembly, 2);
    private static readonly EntityKey AttributeKey = new(Assembly, 3);
    private static readonly CliTypeIdentity Type = CliTypeIdentity.Named(Assembly, "System", "Type", false);
    private static readonly CliTypeIdentity Member = CliTypeIdentity.Named(Assembly, "System.Reflection", "MemberInfo", false);
    private static readonly CliTypeIdentity Filter = CliTypeIdentity.Named(new("Application"), "User", "MarkerAttribute", false);
    private static readonly CliTypeIdentity Boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);

    [Theory]
    [InlineData("IsDefined")]
    [InlineData("GetCustomAttributes")]
    public void RecognizesFilteredInstanceQueries(string name)
    {
        var method = Method(InstanceKey, name, false, [], [Type, Boolean]);

        var result = Classify(Create(), method);

        var operation = name == "IsDefined" ? AttributeQueryOperation.IsDefined : AttributeQueryOperation.GetMany;
        Assert.Equal(new(operation, 3, 1, null, 2), result);
    }

    [Theory]
    [InlineData("GetCustomAttribute", false)]
    [InlineData("GetCustomAttribute", true)]
    [InlineData("GetCustomAttributes", false)]
    [InlineData("GetCustomAttributes", true)]
    public void GenericExtensionsPreserveUserDefinedFilterIdentityAndInheritance(string name, bool inherit)
    {
        var method = Method(ExtensionKey, name, true, [Filter], inherit ? [Member, Boolean] : [Member]);

        var result = Classify(Create(), method);

        Assert.NotNull(result);
        Assert.Equal(Filter, result.GenericFilter);
        Assert.Null(result.FilterArgument);
        Assert.Equal(inherit ? 2 : 1, result.ArgumentCount);
        Assert.Equal(inherit ? 1 : (int?)null, result.InheritArgument);
        Assert.Equal(name == "GetCustomAttribute" ? AttributeQueryOperation.GetOne : AttributeQueryOperation.GetMany,
            result.Operation);
    }

    [Theory]
    [InlineData("IsDefined", false, false)]
    [InlineData("IsDefined", true, true)]
    [InlineData("GetCustomAttribute", false, true)]
    [InlineData("GetCustomAttribute", true, false)]
    [InlineData("GetCustomAttributes", false, false)]
    [InlineData("GetCustomAttributes", true, true)]
    public void StaticHelpersAndNonGenericExtensionsShareExplicitFilterBounds(string name, bool extension, bool inherit)
    {
        var method = Method(extension ? ExtensionKey : AttributeKey, name, true, [],
            inherit ? [Member, Type, Boolean] : [Member, Type]);

        var result = Classify(Create(), method);

        Assert.NotNull(result);
        Assert.Null(result.GenericFilter);
        Assert.Equal(1, result.FilterArgument);
        Assert.Equal(inherit ? 3 : 2, result.ArgumentCount);
        Assert.Equal(inherit ? 2 : (int?)null, result.InheritArgument);
    }

    [Fact]
    public void SameSpellingInAnUnrelatedAssemblyIsNotAnIntrinsic()
    {
        var method = Method(new(new("Application"), 1), "GetCustomAttributes", false, [], [Type, Boolean]);

        Assert.Null(Classify(Create(), method));
    }

    [Fact]
    public void UnrelatedMemberOnAKnownTypeIsNotAQuery()
    {
        Assert.Null(Classify(Create(), Method(InstanceKey, "get_Name", false, [], [])));
    }

    [Theory]
    [InlineData("get_CustomAttributes")]
    [InlineData("GetCustomAttributesData")]
    public void MetadataEnumerationUsesItsManagedContract(string name)
    {
        Assert.Null(Classify(Create(), Method(InstanceKey, name, false, [], [])));
    }

    [Theory]
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
    public void OtherOverloadsRetainTheirManagedBodies(int shape)
    {
        var method = shape switch
        {
            0 => Method(InstanceKey, "GetCustomAttributes", false, [], [Boolean]),
            1 => Method(ExtensionKey, "GetCustomAttributes", true, [], [Member]),
            2 => Method(ExtensionKey, "GetCustomAttributes", true, [], [Member, Boolean]),
            3 => Method(InstanceKey, "GetCustomAttributes", true, [], [Type, Boolean]),
            4 => Method(InstanceKey, "GetCustomAttribute", false, [], [Type, Boolean]),
            5 => Method(ExtensionKey, "IsDefined", true, [Filter], [Member]),
            6 => Method(AttributeKey, "GetCustomAttributes", true, [Filter], [Member]),
            7 => Method(ExtensionKey, "GetCustomAttributes", true, [Filter, Filter], [Member]),
            8 => Method(ExtensionKey, "GetCustomAttributes", true, [], [Type, Type]),
            9 => Method(ExtensionKey, "GetCustomAttributes", true, [], [Member, Type, Type]),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        Assert.Null(Classify(Create(), method));
    }

    [Fact]
    public void MissingMethodIsRejected() => Assert.Throws<ArgumentNullException>(() => Classify(Create(), null!));

    private static AttributeQueryClassifier Create() => new(
        ImmutableDictionary<EntityKey, AttributeQueryApiKind>.Empty
            .Add(InstanceKey, AttributeQueryApiKind.Instance)
            .Add(ExtensionKey, AttributeQueryApiKind.Extension)
            .Add(AttributeKey, AttributeQueryApiKind.AttributeStatic), Member, Type);

    private static AttributeQueryCall? Classify<TClassifier>(TClassifier classifier, MethodInstanceModel method)
        where TClassifier : IAttributeQueryClassifier => classifier.Classify(method);

    private static MethodInstanceModel Method(EntityKey owner, string name, bool isStatic,
        ImmutableArray<CliTypeIdentity> arguments, ImmutableArray<CliTypeIdentity> parameters)
    {
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), parameters);
        return new(new(new(Assembly, 20), owner, name, isStatic, signature, 0)
        { GenericArity = arguments.Length }, Type, arguments, signature);
    }
}
