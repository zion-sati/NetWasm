using Mono.Cecil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class CompatibleFrameworkReferenceTypeSelectorTests
{
    [Fact]
    public void RemovesIncompatibleTypeAndDependentMappingsButKeepsIndependentTypes()
    {
        using var source = Assembly("Backport");
        using var platform = Assembly("Platform");
        var sourceBase = Add(source, "Base");
        var targetBase = Add(platform, "Base");
        targetBase.IsInterface = true;
        Add(source, "Derived").BaseType = sourceBase;
        Add(platform, "Derived").BaseType = targetBase;
        Add(source, "Independent");
        Add(platform, "Independent");
        var selector = Create();

        Assert.Equal(["System.Independent"], selector.SelectTypes(new("Backport", source, platform)));
        Assert.Contains(source.MainModule.Types, type => type.Name == "Base");
        Assert.Contains(source.MainModule.Types, type => type.Name == "Derived");
        using var sourceImage = new MemoryStream();
        using var targetImage = new MemoryStream();
        source.Write(sourceImage);
        platform.Write(targetImage);
        var builder = new FrameworkReferenceViewBuilder(new FrameworkReferenceTypeRewriter(),
            new FrameworkTypeContractChecker(new FrameworkContractTypeComparer(),
                new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer())));
        var image = builder.Build(new(sourceImage.ToArray(), targetImage.ToArray(),
            selector.SelectTypes(new("Backport", source, platform))));
        using var view = AssemblyDefinition.ReadAssembly(new MemoryStream(image));
        Assert.Equal("System.Independent", Assert.Single(view.MainModule.ExportedTypes).FullName);
        Assert.Contains(view.MainModule.Types, type => type.Name == "Base");
        Assert.Equal("System.Base", view.MainModule.Types.Single(type => type.Name == "Derived").BaseType.FullName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NestedForwardersAndVisibleParentAreSelectedTogether(bool incompatibleParent)
    {
        using var source = Assembly("Backport");
        using var platform = Assembly("Platform");
        var parent = Add(source, "Parent");
        var target = Add(platform, "Parent");
        var nested = new TypeDefinition("", "Nested", TypeAttributes.NestedPublic, source.MainModule.TypeSystem.Object);
        parent.NestedTypes.Add(nested);
        var nestedTarget = new TypeDefinition("", "Nested", TypeAttributes.NestedPublic, platform.MainModule.TypeSystem.Object);
        target.NestedTypes.Add(nestedTarget);
        if (incompatibleParent) target.IsInterface = true;
        else nestedTarget.IsInterface = true;
        Assert.Empty(Create().SelectTypes(new("Backport", source, platform)));
    }

    [Fact]
    public void EmptyOwnershipSetDoesNotCheckContracts()
    {
        using var source = Assembly("Unrelated");
        using var target = Assembly("Platform");
        var checker = new RejectUnexpectedCheck();
        var selector = new CompatibleFrameworkReferenceTypeSelector(new FrameworkReferenceTypeSelector(), checker);
        Assert.Empty(selector.SelectTypes(new("Unrelated", source, target)));
    }

    private static CompatibleFrameworkReferenceTypeSelector Create() => new(new AllOverlaps(),
        new FrameworkTypeContractChecker(new FrameworkContractTypeComparer(),
            new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer())));

    private static AssemblyDefinition Assembly(string name) =>
        AssemblyDefinition.CreateAssembly(new(name, new Version(1, 0)), name, ModuleKind.Dll);

    private static TypeDefinition Add(AssemblyDefinition assembly, string name)
    {
        var type = new TypeDefinition("System", name, TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(type);
        return type;
    }

    private sealed class AllOverlaps : IFrameworkReferenceTypeSelector
    {
        public IReadOnlyList<string> SelectTypes(FrameworkReferenceSelection request) => request.Implementation.MainModule.GetTypes()
            .Where(type => type.Namespace == "System" || type.IsNestedPublic).Select(type => type.FullName).ToArray();
    }

    private sealed class RejectUnexpectedCheck : IFrameworkTypeContractChecker
    {
        public FrameworkTypeMismatch Check(FrameworkTypeContract request) => throw new InvalidOperationException("Unexpected contract check.");
    }
}
