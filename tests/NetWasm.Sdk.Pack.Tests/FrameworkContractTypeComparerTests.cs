using System.Collections.Frozen;
using Mono.Cecil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class FrameworkContractTypeComparerTests
{
    [Theory]
    [InlineData("cc7b13ffcd2ddd51", true)]
    [InlineData("b03f5f7f11d50a3a", false)]
    [InlineData("", false)]
    public void RecognizesStandardScopeOnlyWithTheExpectedBindingKey(string token, bool expected)
    {
        using var source = Assembly("Backport");
        using var platform = Assembly("Platform");
        var contract = new AssemblyNameReference("netstandard", new Version(2, 0))
        { PublicKeyToken = Convert.FromHexString(token) };
        var left = new TypeReference("System", "Contract", source.MainModule, contract);
        var right = new TypeReference("System", "Contract", platform.MainModule, platform.Name);
        var scope = Scope(source, platform, [], ["System.Contract"]);
        var comparer = Assert.IsAssignableFrom<IFrameworkContractTypeComparer>(new FrameworkContractTypeComparer());
        Assert.Equal(expected, comparer.Compare(left, right, scope));
        Assert.False(comparer.Compare(left, right, scope with { PlatformTypeNames = Array.Empty<string>().ToFrozenSet() }));
    }

    [Fact]
    public void ExplicitMappedNamesUnifyButUnrelatedOwnersRemainDistinct()
    {
        using var source = Assembly("Backport");
        using var platform = Assembly("Platform");
        var left = new TypeReference("Example", "Shared", source.MainModule, source.Name);
        var right = new TypeReference("Example", "Shared", platform.MainModule, platform.Name);
        var scope = Scope(source, platform, ["Example.Shared"], ["Example.Shared"]);
        var comparer = new FrameworkContractTypeComparer();
        Assert.True(comparer.Compare(left, right, scope));
        left.Scope = new AssemblyNameReference("Unrelated", new Version(1, 0));
        Assert.False(comparer.Compare(left, right, scope));
        right.Scope = new AssemblyNameReference("Unrelated", new Version(1, 0));
        Assert.True(comparer.Compare(left, right, scope));
        right.Scope = new AssemblyNameReference("Unrelated", new Version(2, 0));
        Assert.False(comparer.Compare(left, right, scope));
    }

    [Fact]
    public void PreservesArrayBoundsModifiersAndGenericParameterKind()
    {
        using var source = Assembly("Backport");
        using var platform = Assembly("Platform");
        var scope = Scope(source, platform, [], []);
        var comparer = new FrameworkContractTypeComparer();
        var element = source.MainModule.TypeSystem.Int32;
        var array = new ArrayType(element, 2);
        var other = new ArrayType(element, 2);
        Assert.True(comparer.Compare(array, other, scope));
        other.Dimensions[0] = new ArrayDimension(1, 5);
        Assert.False(comparer.Compare(array, other, scope));
        Assert.False(comparer.Compare(new ArrayType(element), array, scope));
        var modifier = new TypeReference("Example", "Modifier", source.MainModule, source.Name);
        Assert.True(comparer.Compare(new RequiredModifierType(modifier, element), new RequiredModifierType(modifier, element), scope));
        Assert.False(comparer.Compare(new RequiredModifierType(modifier, element), new OptionalModifierType(modifier, element), scope));
        var type = new TypeReference("Example", "Owner", source.MainModule, source.Name);
        var method = new MethodReference("Run", element, type);
        var typeParameter = new GenericParameter("T", type);
        type.GenericParameters.Add(typeParameter);
        var methodParameter = new GenericParameter("T", method);
        method.GenericParameters.Add(methodParameter);
        Assert.False(comparer.Compare(typeParameter, methodParameter, scope));
        Assert.True(comparer.Compare(typeParameter, typeParameter, scope));
        Assert.True(comparer.Compare(null, null, scope));
        Assert.False(comparer.Compare(null, element, scope));
    }

    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("System.Reflection")]
    public void KnownSourceScopeDoesNotOverrideRejectedTypeMapping(string name)
    {
        using var source = Assembly(name);
        source.Name.PublicKeyToken = Convert.FromHexString("b03f5f7f11d50a3a");
        using var platform = Assembly("Platform");
        var left = new TypeReference("System", "Contract", source.MainModule, source.Name);
        var right = new TypeReference("System", "Contract", platform.MainModule, platform.Name);
        var comparer = new FrameworkContractTypeComparer();
        Assert.False(comparer.Compare(left, right, Scope(source, platform, [], ["System.Contract"])));
        Assert.True(comparer.Compare(left, right, Scope(source, platform, ["System.Contract"], ["System.Contract"])));
    }

    private static AssemblyDefinition Assembly(string name) =>
        AssemblyDefinition.CreateAssembly(new(name, new Version(1, 0)), name, ModuleKind.Dll);

    private static FrameworkContractScope Scope(AssemblyDefinition source, AssemblyDefinition platform,
        string[] mapped, string[] known) => new(source.Name, platform.Name,
            mapped.ToFrozenSet(StringComparer.Ordinal), known.ToFrozenSet(StringComparer.Ordinal));
}
