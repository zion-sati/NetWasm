using Mono.Cecil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class FrameworkReferenceTypeSelectorTests
{
    [Theory]
    [InlineData("System.Runtime", "b03f5f7f11d50a3a")]
    [InlineData("System.Reflection", "b03f5f7f11d50a3a")]
    [InlineData("System.Collections", "b03f5f7f11d50a3a")]
    [InlineData("System.Memory", "cc7b13ffcd2ddd51")]
    [InlineData("System.Buffers", "cc7b13ffcd2ddd51")]
    [InlineData("System.Threading.Tasks.Extensions", "cc7b13ffcd2ddd51")]
    [InlineData("System.Runtime.CompilerServices.Unsafe", "b03f5f7f11d50a3a")]
    [InlineData("System.Numerics.Vectors", "b03f5f7f11d50a3a")]
    [InlineData("Microsoft.Bcl.AsyncInterfaces", "cc7b13ffcd2ddd51")]
    [InlineData("Microsoft.Bcl.HashCode", "cc7b13ffcd2ddd51")]
    public void SelectsOnlyVisibleFrameworkOverlaps(string name, string token)
    {
        using var source = Assembly(name);
        using var platform = Assembly("NetWasm.CoreLib");
        source.Name.PublicKeyToken = Convert.FromHexString(token);
        var shared = AddType(source, "System", "Shared");
        var target = AddType(platform, "System", "Shared");
        shared.NestedTypes.Add(new("", "Nested", TypeAttributes.NestedPublic));
        target.NestedTypes.Add(new("", "Nested", TypeAttributes.NestedPublic));
        AddType(source, "System", "PackageOnly");
        AddType(source, "Example", "Unrelated");
        AddType(platform, "Example", "Unrelated");
        AddType(source, "System", "Private").IsPublic = false;
        AddType(platform, "System", "Private");
        AddType(source, "System", "PrivateOwner");
        AddType(platform, "System", "PrivateOwner").IsPublic = false;
        var selector = Assert.IsAssignableFrom<IFrameworkReferenceTypeSelector>(new FrameworkReferenceTypeSelector());

        var selected = selector.SelectTypes(new(name.ToLowerInvariant(), source, platform));

        Assert.Equal(["System.Shared", "System.Shared/Nested"], selected);
        Assert.Contains(source.MainModule.Types, type => type.Name == "PackageOnly");
        Assert.Equal(new Version(4, 0, 0, 0), source.Name.Version);
    }

    [Theory]
    [InlineData("Unrelated", "Unrelated", "cc7b13ffcd2ddd51", "")]
    [InlineData("System.Memory", "Unrelated", "cc7b13ffcd2ddd51", "")]
    [InlineData("System.Memory", "System.Memory", "", "")]
    [InlineData("System.Memory", "System.Memory", "b03f5f7f11d50a3a", "")]
    [InlineData("System.Memory", "System.Memory", "cc7b13ffcd2ddd51", "en-US")]
    [InlineData("Microsoft.Bcl.AsyncInterfaces", "Microsoft.Bcl.AsyncInterfaces", "b03f5f7f11d50a3a", "")]
    public void DoesNotMapUnrecognizedBindingIdentities(string name, string package, string token, string culture)
    {
        using var source = Assembly(name);
        using var platform = Assembly("NetWasm.CoreLib");
        source.Name.PublicKeyToken = Convert.FromHexString(token);
        source.Name.Culture = culture;
        AddType(source, "System", "Memory`1");
        AddType(platform, "System", "Memory`1");
        Assert.Empty(new FrameworkReferenceTypeSelector().SelectTypes(new(package, source, platform)));
    }

    [Fact]
    public void RejectsWrongPlatformOwnerAndNullInputs()
    {
        using var source = Assembly("System.Memory");
        using var platform = Assembly("Other");
        source.Name.PublicKeyToken = Convert.FromHexString("cc7b13ffcd2ddd51");
        var selector = new FrameworkReferenceTypeSelector();
        Assert.Throws<ArgumentException>(() => selector.SelectTypes(new("System.Memory", source, platform)));
        Assert.Throws<ArgumentNullException>(() => selector.SelectTypes(null!));
        Assert.Throws<ArgumentNullException>(() => selector.SelectTypes(new(null!, source, platform)));
        Assert.Throws<ArgumentNullException>(() => selector.SelectTypes(new("System.Memory", null!, platform)));
        Assert.Throws<ArgumentNullException>(() => selector.SelectTypes(new("System.Memory", source, null!)));
    }

    private static AssemblyDefinition Assembly(string name) =>
        AssemblyDefinition.CreateAssembly(new(name, new Version(4, 0, 0, 0)), name, ModuleKind.Dll);

    private static TypeDefinition AddType(AssemblyDefinition assembly, string ns, string name)
    {
        var type = new TypeDefinition(ns, name, TypeAttributes.Public);
        assembly.MainModule.Types.Add(type);
        return type;
    }
}
