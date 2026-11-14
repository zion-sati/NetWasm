using Mono.Cecil;
using Mono.Cecil.Cil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class FrameworkReferenceViewBuilderTests
{
    [Fact]
    public void ViewForwardsSharedTypesAndPreservesPackageOnlySignatures()
    {
        using var original = Fixture("Backport");
        using var platform = Fixture("Platform");
        var shared = original.MainModule.Types.Single(type => type.Name == "Shared");
        var unique = new TypeDefinition("Example", "Unique", TypeAttributes.Public, original.MainModule.TypeSystem.Object);
        original.MainModule.Types.Add(unique);
        var method = new MethodDefinition("Read", MethodAttributes.Public | MethodAttributes.Static,
            new ArrayType(shared));
        method.Parameters.Add(new ParameterDefinition(new ByReferenceType(shared)));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        unique.Methods.Add(method);
        var input = Bytes(original);
        var saved = input.ToArray();

        var result = CreateBuilder().Build(new(input, Bytes(platform), ["Example.Shared"]));
        using var view = AssemblyDefinition.ReadAssembly(new MemoryStream(result));

        Assert.Equal(saved, input);
        Assert.Equal("Backport", view.Name.Name);
        Assert.DoesNotContain(view.MainModule.Types, type => type.Name == "Shared");
        var forward = Assert.Single(view.MainModule.ExportedTypes);
        Assert.True(forward.IsForwarder);
        Assert.Equal("Platform", forward.Scope.Name);
        var retained = view.MainModule.Types.Single(type => type.Name == "Unique");
        var declaration = Assert.Single(retained.Methods);
        Assert.False(declaration.HasBody);
        Assert.Equal("Platform", declaration.ReturnType.GetElementType().Scope.Name);
        Assert.Equal("Platform", declaration.Parameters[0].ParameterType.GetElementType().Scope.Name);
    }

    [Theory]
    [InlineData("Other", 1)]
    [InlineData("Backport", 2)]
    public void SameNamedTypesFromAnotherAssemblyAreNotRedirected(string name, int version)
    {
        using var original = Fixture("Backport");
        using var platform = Fixture("Platform");
        var unique = new TypeDefinition("Example", "Unique", TypeAttributes.Public, original.MainModule.TypeSystem.Object);
        var other = new AssemblyNameReference(name, new Version(version, 0));
        original.MainModule.AssemblyReferences.Add(other);
        unique.Fields.Add(new FieldDefinition("Other", FieldAttributes.Public,
            new TypeReference("Example", "Shared", original.MainModule, other)));
        original.MainModule.Types.Add(unique);

        var input = Bytes(original);
        using var before = AssemblyDefinition.ReadAssembly(new MemoryStream(input));
        Assert.Equal(other.FullName, Assert.IsType<AssemblyNameReference>(before.MainModule.Types
            .Single(type => type.Name == "Unique").Fields.Single().FieldType.Scope).FullName);
        using var view = AssemblyDefinition.ReadAssembly(new MemoryStream(CreateBuilder()
            .Build(new(input, Bytes(platform), ["Example.Shared"]))));

        Assert.Equal(other.FullName, Assert.IsType<AssemblyNameReference>(view.MainModule.Types
            .Single(type => type.Name == "Unique").Fields.Single().FieldType.Scope).FullName);
    }

    [Fact]
    public void AttributeConstructorFollowsForwarderWithoutChangingArguments()
    {
        using var source = Fixture("Backport");
        using var platform = Fixture("Platform");
        var shared = source.MainModule.Types.Single(type => type.Name == "Shared");
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public |
            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, source.MainModule.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        shared.Methods.Add(constructor);
        byte[] blob = [1, 0, 0, 0];
        source.CustomAttributes.Add(new CustomAttribute(constructor, blob));

        using var view = AssemblyDefinition.ReadAssembly(new MemoryStream(CreateBuilder()
            .Build(new(Bytes(source), Bytes(platform), ["Example.Shared"]))));

        var attribute = Assert.Single(view.CustomAttributes);
        Assert.Equal("Platform", attribute.Constructor.DeclaringType.Scope.Name);
        Assert.Equal(blob, attribute.GetBlob());
    }

    [Fact]
    public void ProjectionIsDeterministic()
    {
        using var source = Fixture("Backport");
        using var platform = Fixture("Platform");
        var request = new FrameworkReferenceViewRequest(Bytes(source), Bytes(platform), ["Example.Shared"]);
        var builder = CreateBuilder();
        Assert.Equal(builder.Build(request), builder.Build(request));
    }

    [Fact]
    public void IncompatibleKindAndStricterConstraintsAreRejected()
    {
        using var source = Fixture("Backport");
        using var platform = Fixture("Platform");
        var shared = platform.MainModule.Types.Single(type => type.Name == "Shared");
        shared.IsInterface = true;
        Assert.Throws<ArgumentException>(() => CreateBuilder()
            .Build(new(Bytes(source), Bytes(platform), ["Example.Shared"])));
        shared.IsInterface = false;
        var sourceType = source.MainModule.Types.Single(type => type.Name == "Shared");
        sourceType.GenericParameters.Add(new GenericParameter("T", sourceType));
        shared.GenericParameters.Add(new GenericParameter("T", shared)
        { Attributes = GenericParameterAttributes.ReferenceTypeConstraint });
        Assert.Throws<ArgumentException>(() => CreateBuilder()
            .Build(new(Bytes(source), Bytes(platform), ["Example.Shared"])));
    }

    [Fact]
    public void UnmappedPublicNestedDeclarationsAreNotSilentlyDropped()
    {
        using var source = Fixture("Backport");
        using var platform = Fixture("Platform");
        source.MainModule.Types.Single(type => type.Name == "Shared").NestedTypes.Add(
            new TypeDefinition("", "Nested", TypeAttributes.NestedPublic, source.MainModule.TypeSystem.Object));
        Assert.Throws<ArgumentException>(() => CreateBuilder()
            .Build(new(Bytes(source), Bytes(platform), ["Example.Shared"])));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void PreservesRefLikeSemantics(bool sourceRefLike, bool targetRefLike, bool accepted)
    {
        using var source = Fixture("Backport");
        using var target = Fixture("Platform");
        foreach (var assembly in new[] { source, target })
        {
            var type = assembly.MainModule.Types.Single(t => t.Name == "Shared");
            type.BaseType = new TypeReference("System", "ValueType", assembly.MainModule,
                assembly.MainModule.TypeSystem.Object.Scope);
            type.IsSealed = true;
        }
        void Mark(AssemblyDefinition assembly)
        {
            var attribute = new TypeReference("System.Runtime.CompilerServices", "IsByRefLikeAttribute",
                assembly.MainModule, assembly.MainModule.TypeSystem.Object.Scope);
            var constructor = new MethodReference(".ctor", assembly.MainModule.TypeSystem.Void, attribute) { HasThis = true };
            assembly.MainModule.Types.Single(type => type.Name == "Shared").CustomAttributes
                .Add(new CustomAttribute(constructor, [1, 0, 0, 0]));
        }
        if (sourceRefLike) Mark(source);
        if (targetRefLike) Mark(target);
        var builder = CreateBuilder();
        var request = new FrameworkReferenceViewRequest(Bytes(source), Bytes(target), ["Example.Shared"]);
        if (accepted) Assert.NotEmpty(builder.Build(request));
        else Assert.Throws<ArgumentException>(() => builder.Build(request));
    }

    [Theory]
    [InlineData(42, 42, true)]
    [InlineData(42, 43, false)]
    [InlineData(42, null, false)]
    public void PreservesEnumValues(int sourceValue, int? targetValue, bool accepted)
    {
        using var source = Fixture("Backport");
        using var target = Fixture("Platform");
        void MakeEnum(AssemblyDefinition assembly, int? value)
        {
            var type = assembly.MainModule.Types.Single(t => t.Name == "Shared");
            type.IsSealed = true;
            type.BaseType = new TypeReference("System", "Enum", assembly.MainModule,
                assembly.MainModule.TypeSystem.Object.Scope);
            type.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName |
                FieldAttributes.RTSpecialName, assembly.MainModule.TypeSystem.Int32));
            if (value.HasValue) type.Fields.Add(new FieldDefinition("Answer",
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal, type)
            { Constant = value.Value });
        }
        MakeEnum(source, sourceValue);
        MakeEnum(target, targetValue);
        var builder = CreateBuilder();
        var request = new FrameworkReferenceViewRequest(Bytes(source), Bytes(target), ["Example.Shared"]);
        if (accepted) Assert.NotEmpty(builder.Build(request));
        else Assert.Throws<ArgumentException>(() => builder.Build(request));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void AllowsRefStructConstraintMayBeAddedButNotRemoved(bool sourceAllows, bool targetAllows, bool accepted)
    {
        using var source = Fixture("Backport");
        using var target = Fixture("Platform");
        foreach (var (assembly, allows) in new[] { (source, sourceAllows), (target, targetAllows) })
        {
            var type = assembly.MainModule.Types.Single(t => t.Name == "Shared");
            type.GenericParameters.Add(new GenericParameter("T", type)
            { Attributes = allows ? (GenericParameterAttributes)0x20 : 0 });
        }
        var builder = CreateBuilder();
        var request = new FrameworkReferenceViewRequest(Bytes(source), Bytes(target), ["Example.Shared"]);
        if (accepted) Assert.NotEmpty(builder.Build(request));
        else Assert.Throws<ArgumentException>(() => builder.Build(request));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiresOriginalInterfaceThroughInheritedGenericBase(bool correctArgument)
    {
        using var source = Fixture("Backport");
        using var target = Fixture("Platform");
        var sourceType = source.MainModule.Types.Single(type => type.Name == "Shared");
        var targetType = target.MainModule.Types.Single(type => type.Name == "Shared");
        var owner = new AssemblyNameReference("Contracts", new Version(1, 0));
        target.MainModule.AssemblyReferences.Add(owner);
        var contract = new TypeReference("Example", "IContract`1", target.MainModule, owner);
        var expected = new GenericInstanceType(source.MainModule.ImportReference(contract));
        expected.GenericArguments.Add(source.MainModule.TypeSystem.Int32);
        sourceType.Interfaces.Add(new InterfaceImplementation(expected));
        var parent = new TypeDefinition("Example", "Parent`1", TypeAttributes.Public, target.MainModule.TypeSystem.Object);
        parent.GenericParameters.Add(new GenericParameter("T", parent));
        target.MainModule.Types.Add(parent);
        var implemented = new GenericInstanceType(contract);
        implemented.GenericArguments.Add(parent.GenericParameters[0]);
        parent.Interfaces.Add(new InterfaceImplementation(implemented));
        var inherited = new GenericInstanceType(parent);
        inherited.GenericArguments.Add(correctArgument ? target.MainModule.TypeSystem.Int32 : target.MainModule.TypeSystem.String);
        targetType.BaseType = inherited;
        var request = new FrameworkReferenceViewRequest(Bytes(source), Bytes(target), ["Example.Shared"]);
        if (correctArgument) Assert.NotEmpty(CreateBuilder().Build(request));
        else Assert.Throws<ArgumentException>(() => CreateBuilder().Build(request));
    }

    private static FrameworkReferenceViewBuilder CreateBuilder() => new(
        new FrameworkReferenceTypeRewriter(),
        new FrameworkTypeContractChecker(new FrameworkContractTypeComparer(),
            new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer())));

    private static AssemblyDefinition Fixture(string name)
    {
        var assembly = AssemblyDefinition.CreateAssembly(new(name, new Version(1, 0)), name, ModuleKind.Dll);
        assembly.MainModule.Types.Add(new TypeDefinition("Example", "Shared", TypeAttributes.Public,
            assembly.MainModule.TypeSystem.Object));
        return assembly;
    }

    private static byte[] Bytes(AssemblyDefinition assembly)
    {
        using var stream = new MemoryStream();
        assembly.Write(stream);
        return stream.ToArray();
    }
}
