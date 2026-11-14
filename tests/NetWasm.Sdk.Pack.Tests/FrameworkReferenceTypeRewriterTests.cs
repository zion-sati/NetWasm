using Mono.Cecil;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class FrameworkReferenceTypeRewriterTests
{
    [Fact]
    public void GenericMethodParametersBelongToTheRewrittenMethod()
    {
        using var module = ModuleDefinition.CreateModule("Source", ModuleKind.Dll);
        var owner = new TypeDefinition("Example", "Owner", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(owner);
        var method = new MethodReference("Echo", module.TypeSystem.Void, owner) { HasThis = true };
        var parameter = new GenericParameter("T", method);
        method.GenericParameters.Add(parameter);
        method.ReturnType = new ArrayType(parameter);
        method.Parameters.Add(new ParameterDefinition(new ByReferenceType(parameter)));
        var strategy = Assert.IsAssignableFrom<IFrameworkReferenceTypeRewriter>(new FrameworkReferenceTypeRewriter());

        var result = strategy.Rewrite(method, new(module, new Dictionary<string, TypeReference>()));

        var rewritten = Assert.Single(result.GenericParameters);
        Assert.Same(result, rewritten.Owner);
        Assert.Same(rewritten, result.ReturnType.GetElementType());
        Assert.Same(rewritten, Assert.Single(result.Parameters).ParameterType.GetElementType());
        Assert.Same(method, parameter.Owner);
        Assert.True(result.HasThis);
    }

    [Fact]
    public void GenericMethodInstanceRetainsItsArgumentsAndMappedDeclaringType()
    {
        using var module = ModuleDefinition.CreateModule("Source", ModuleKind.Dll);
        var owner = new TypeDefinition("Example", "Owner", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(owner);
        var destination = new TypeReference("Example", "Owner", module, new AssemblyNameReference("Platform", new Version(1, 0)));
        var method = new MethodReference("Echo", module.TypeSystem.Void, owner);
        var parameter = new GenericParameter("T", method);
        method.GenericParameters.Add(parameter);
        method.ReturnType = parameter;
        var instance = new GenericInstanceMethod(method);
        instance.GenericArguments.Add(owner);
        var strategy = Assert.IsAssignableFrom<IFrameworkReferenceTypeRewriter>(new FrameworkReferenceTypeRewriter());

        var result = Assert.IsType<GenericInstanceMethod>(strategy.Rewrite(instance,
            new(module, new Dictionary<string, TypeReference> { [owner.FullName] = destination })));

        Assert.Same(destination, Assert.Single(result.GenericArguments));
        Assert.Same(destination, result.ElementMethod.DeclaringType);
        Assert.Same(result.ElementMethod.GenericParameters[0], result.ElementMethod.ReturnType);
        Assert.Same(owner, instance.GenericArguments[0]);
    }

    [Fact]
    public void FunctionPointerPreservesModifiersArrayBoundsAndCallingConvention()
    {
        using var module = ModuleDefinition.CreateModule("Source", ModuleKind.Dll);
        var source = new TypeDefinition("Example", "Shared", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(source);
        var destination = new TypeReference("Example", "Shared", module, new AssemblyNameReference("Platform", new Version(1, 0)));
        var array = new ArrayType(source, 2);
        array.Dimensions[0] = new ArrayDimension(1, 5);
        array.Dimensions[1] = new ArrayDimension(2, 7);
        var function = new FunctionPointerType
        {
            CallingConvention = MethodCallingConvention.C,
            ReturnType = new RequiredModifierType(source, new PointerType(source))
        };
        function.Parameters.Add(new ParameterDefinition(new OptionalModifierType(source, array)));
        var strategy = Assert.IsAssignableFrom<IFrameworkReferenceTypeRewriter>(new FrameworkReferenceTypeRewriter());

        var result = Assert.IsType<FunctionPointerType>(strategy.Rewrite(function,
            new(module, new Dictionary<string, TypeReference> { [source.FullName] = destination })));

        Assert.Equal(MethodCallingConvention.C, result.CallingConvention);
        var required = Assert.IsType<RequiredModifierType>(result.ReturnType);
        Assert.Same(destination, required.ModifierType);
        Assert.Same(destination, Assert.IsType<PointerType>(required.ElementType).ElementType);
        var optional = Assert.IsType<OptionalModifierType>(Assert.Single(result.Parameters).ParameterType);
        Assert.Same(destination, optional.ModifierType);
        var rewrittenArray = Assert.IsType<ArrayType>(optional.ElementType);
        Assert.Equal(2, rewrittenArray.Rank);
        Assert.Equal(1, rewrittenArray.Dimensions[0].LowerBound);
        Assert.Equal(5, rewrittenArray.Dimensions[0].UpperBound);
        Assert.Equal(2, rewrittenArray.Dimensions[1].LowerBound);
        Assert.Equal(7, rewrittenArray.Dimensions[1].UpperBound);
        Assert.Same(destination, rewrittenArray.ElementType);
        Assert.Same(source, array.ElementType);
    }
}
