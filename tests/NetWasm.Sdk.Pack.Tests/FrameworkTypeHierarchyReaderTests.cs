using System.Collections.Frozen;
using Mono.Cecil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class FrameworkTypeHierarchyReaderTests
{
    [Fact]
    public void SubstitutesArgumentsThroughBaseAndInterfaceAncestors()
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new("Platform", new Version(1, 0)), "Platform", ModuleKind.Dll);
        var module = assembly.MainModule;
        var contract = Add("IContract", true);
        var parent = Add("Parent", false);
        var root = Add("Root", false);
        var inherited = new GenericInstanceType(contract);
        inherited.GenericArguments.Add(parent.GenericParameters[0]);
        parent.Interfaces.Add(new InterfaceImplementation(inherited));
        var baseType = new GenericInstanceType(parent);
        baseType.GenericArguments.Add(new ArrayType(root.GenericParameters[0]));
        root.BaseType = baseType;
        var result = Read(root, assembly);
        Assert.Same(baseType.ElementType, Assert.IsType<GenericInstanceType>(Assert.Single(result.BaseTypes)).ElementType);
        var implemented = Assert.IsType<GenericInstanceType>(Assert.Single(result.Interfaces));
        Assert.Same(root.GenericParameters[0], Assert.IsType<ArrayType>(Assert.Single(implemented.GenericArguments)).ElementType);

        TypeDefinition Add(string name, bool isInterface)
        {
            var type = new TypeDefinition("Example", name + "`1", TypeAttributes.Public |
                (isInterface ? TypeAttributes.Interface | TypeAttributes.Abstract : 0));
            type.GenericParameters.Add(new GenericParameter("T", type));
            module.Types.Add(type);
            return type;
        }
    }

    [Fact]
    public void DoesNotResolveUnrelatedSameNamedOwners()
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new("Platform", new Version(1, 0)), "Platform", ModuleKind.Dll);
        var root = new TypeDefinition("Example", "Root", TypeAttributes.Public);
        var parent = new TypeDefinition("Example", "Parent", TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(root);
        assembly.MainModule.Types.Add(parent);
        root.BaseType = new TypeReference("Example", "Parent", assembly.MainModule, new AssemblyNameReference("Other", new Version(1, 0)));
        Assert.Same(root.BaseType, Assert.Single(Read(root, assembly).BaseTypes));
    }

    [Fact]
    public void RejectsCyclesButAllowsRepeatedDiamondInterfaces()
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new("Platform", new Version(1, 0)), "Platform", ModuleKind.Dll);
        var root = new TypeDefinition("Example", "Root", TypeAttributes.Public);
        var contract = new TypeDefinition("Example", "IContract", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        assembly.MainModule.Types.Add(root);
        assembly.MainModule.Types.Add(contract);
        root.Interfaces.Add(new InterfaceImplementation(contract));
        root.Interfaces.Add(new InterfaceImplementation(contract));
        Assert.Equal(2, Read(root, assembly).Interfaces.Count);
        contract.Interfaces.Add(new InterfaceImplementation(contract));
        Assert.Throws<ArgumentException>(() => Read(root, assembly));
    }

    private static FrameworkTypeHierarchy Read(TypeDefinition root, AssemblyDefinition assembly)
    {
        var types = assembly.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var scope = new FrameworkContractScope(assembly.Name, assembly.Name,
            Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal), types.Keys.ToFrozenSet(StringComparer.Ordinal));
        var reader = new FrameworkTypeHierarchyReader(
            new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer());
        return reader.Read(new(root, types, scope));
    }
}
