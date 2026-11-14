using System.Collections.Frozen;
using Mono.Cecil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class FrameworkTypeContractCheckerTests
{
    [Theory]
    [InlineData(false, false, FrameworkTypeMismatch.None)]
    [InlineData(false, true, FrameworkTypeMismatch.Declaration)]
    [InlineData(true, false, FrameworkTypeMismatch.None)]
    [InlineData(true, true, FrameworkTypeMismatch.None)]
    public void ImplementationCannotRemoveReferenceTypeConstructibility(bool sourceAbstract,
        bool targetAbstract, FrameworkTypeMismatch expected)
    {
        using var source = AssemblyDefinition.CreateAssembly(new("Source", new Version(1, 0)), "Source", ModuleKind.Dll);
        using var target = AssemblyDefinition.CreateAssembly(new("Target", new Version(1, 0)), "Target", ModuleKind.Dll);
        var original = new TypeDefinition("Example", "Shared", TypeAttributes.Public, source.MainModule.TypeSystem.Object)
        {
            IsAbstract = sourceAbstract,
        };
        var replacement = new TypeDefinition("Example", "Shared", TypeAttributes.Public, target.MainModule.TypeSystem.Object)
        {
            IsAbstract = targetAbstract,
        };
        source.MainModule.Types.Add(original);
        target.MainModule.Types.Add(replacement);
        var types = target.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var scope = new FrameworkContractScope(source.Name, target.Name, types.Keys.ToFrozenSet(StringComparer.Ordinal), types.Keys.ToFrozenSet(StringComparer.Ordinal));
        var checker = Assert.IsAssignableFrom<IFrameworkTypeContractChecker>(new FrameworkTypeContractChecker(
            new FrameworkContractTypeComparer(), new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer())));
        Assert.Equal(expected, checker.Check(new(original, replacement, types, scope)));
    }

    [Theory]
    [InlineData(false, FrameworkTypeMismatch.GenericConstraints)]
    [InlineData(true, FrameworkTypeMismatch.None)]
    public void AddedVarianceRequiresADelegateOrInterface(bool isDelegate, FrameworkTypeMismatch expected)
    {
        using var source = AssemblyDefinition.CreateAssembly(new("Source", new Version(1, 0)), "Source", ModuleKind.Dll);
        using var target = AssemblyDefinition.CreateAssembly(new("Target", new Version(1, 0)), "Target", ModuleKind.Dll);
        var sourceBase = isDelegate ? new TypeReference("System", "MulticastDelegate", source.MainModule, source.MainModule.TypeSystem.Object.Scope) : source.MainModule.TypeSystem.Object;
        var targetBase = isDelegate ? new TypeReference("System", "MulticastDelegate", target.MainModule, target.MainModule.TypeSystem.Object.Scope) : target.MainModule.TypeSystem.Object;
        var original = new TypeDefinition("Example", "Shared`1", TypeAttributes.Public, sourceBase);
        var replacement = new TypeDefinition("Example", "Shared`1", TypeAttributes.Public, targetBase);
        source.MainModule.Types.Add(original);
        target.MainModule.Types.Add(replacement);
        original.GenericParameters.Add(new GenericParameter("T", original));
        replacement.GenericParameters.Add(new GenericParameter("T", replacement) { Attributes = GenericParameterAttributes.Contravariant });
        var types = target.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var scope = new FrameworkContractScope(source.Name, target.Name, types.Keys.ToFrozenSet(StringComparer.Ordinal), types.Keys.ToFrozenSet(StringComparer.Ordinal));
        var checker = Assert.IsAssignableFrom<IFrameworkTypeContractChecker>(new FrameworkTypeContractChecker(
            new FrameworkContractTypeComparer(), new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer())));
        Assert.Equal(expected, checker.Check(new(original, replacement, types, scope)));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(0, 2, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(2, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(0, 3, false)]
    [InlineData(3, 0, false)]
    [InlineData(3, 3, false)]
    public void VarianceMayAddButNeverRemoveOrReversePromisedConversions(int originalVariance, int replacementVariance, bool compatible)
    {
        using var source = AssemblyDefinition.CreateAssembly(new("Source", new Version(1, 0)), "Source", ModuleKind.Dll);
        using var target = AssemblyDefinition.CreateAssembly(new("Target", new Version(1, 0)), "Target", ModuleKind.Dll);
        var flags = TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract;
        var original = new TypeDefinition("Example", "Shared`1", flags);
        var replacement = new TypeDefinition("Example", "Shared`1", flags);
        source.MainModule.Types.Add(original);
        target.MainModule.Types.Add(replacement);
        original.GenericParameters.Add(new GenericParameter("T", original) { Attributes = (GenericParameterAttributes)originalVariance });
        replacement.GenericParameters.Add(new GenericParameter("T", replacement) { Attributes = (GenericParameterAttributes)replacementVariance });
        var types = target.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var scope = new FrameworkContractScope(source.Name, target.Name, types.Keys.ToFrozenSet(StringComparer.Ordinal), types.Keys.ToFrozenSet(StringComparer.Ordinal));
        var checker = Assert.IsAssignableFrom<IFrameworkTypeContractChecker>(new FrameworkTypeContractChecker(
            new FrameworkContractTypeComparer(), new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer())));
        Assert.Equal(compatible ? FrameworkTypeMismatch.None : FrameworkTypeMismatch.GenericConstraints,
            checker.Check(new(original, replacement, types, scope)));
    }

    [Theory]
    [InlineData(false, FrameworkTypeMismatch.None)]
    [InlineData(true, FrameworkTypeMismatch.Declaration)]
    public void ReturnsTypedCompatibilityDecision(bool makeInterface, FrameworkTypeMismatch expected)
    {
        using var source = AssemblyDefinition.CreateAssembly(new("Source", new Version(1, 0)), "Source", ModuleKind.Dll);
        using var target = AssemblyDefinition.CreateAssembly(new("Target", new Version(1, 0)), "Target", ModuleKind.Dll);
        var original = new TypeDefinition("Example", "Shared", TypeAttributes.Public, source.MainModule.TypeSystem.Object);
        var replacement = new TypeDefinition("Example", "Shared", TypeAttributes.Public, target.MainModule.TypeSystem.Object);
        source.MainModule.Types.Add(original);
        target.MainModule.Types.Add(replacement);
        replacement.IsInterface = makeInterface;
        var types = target.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var scope = new FrameworkContractScope(source.Name, target.Name, types.Keys.ToFrozenSet(StringComparer.Ordinal), types.Keys.ToFrozenSet(StringComparer.Ordinal));
        var checker = Assert.IsAssignableFrom<IFrameworkTypeContractChecker>(new FrameworkTypeContractChecker(
            new FrameworkContractTypeComparer(), new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer())));
        Assert.Equal(expected, checker.Check(new(original, replacement, types, scope)));
    }
}
