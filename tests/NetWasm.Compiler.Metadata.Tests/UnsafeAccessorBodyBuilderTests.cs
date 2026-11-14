using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class UnsafeAccessorBodyBuilderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ConstructorForwardsEveryArgumentAndReturnsAllocatedObject(int count)
    {
        var accessor = Method("Create", true, count);
        var target = Method(".ctor", false, count);
        var body = Builder().Build(accessor, new UnsafeAccessorBinding.Constructor(target));

        AssertBody(body, accessor, Math.Max(1, count));
        Assert.Equal(Enumerable.Range(0, count), body.Instructions.Take(count)
            .Select(instruction => Assert.IsType<CilOperand.Index>(instruction.Operand).Value));
        Assert.All(body.Instructions.Take(count), instruction => Assert.Equal(CilOperation.LoadArgument, instruction.Operation));
        Assert.Equal(CilOperation.NewObject, body.Instructions[^2].Operation);
        Assert.Same(target, Assert.IsType<CilOperand.MethodInstance>(body.Instructions[^2].Operand).Value);
        Assert.Equal(CilOperation.Return, body.Instructions[^1].Operation);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void MethodPreservesInstanceNullCheckAndSkipsOnlyStaticOwner(bool isStatic, int count)
    {
        var accessor = Method("Access", true, count);
        var target = Method("Target", isStatic, count - 1);
        var body = Builder().Build(accessor, new UnsafeAccessorBinding.Method(target));
        var first = isStatic ? 1 : 0;

        AssertBody(body, accessor, Math.Max(1, count - first));
        Assert.Equal(Enumerable.Range(first, count - first), body.Instructions[..^2]
            .Select(instruction => Assert.IsType<CilOperand.Index>(instruction.Operand).Value));
        Assert.All(body.Instructions[..^2], instruction => Assert.Equal(CilOperation.LoadArgument, instruction.Operation));
        Assert.Equal(isStatic ? CilOperation.Call : CilOperation.CallVirtual, body.Instructions[^2].Operation);
        Assert.Same(target, Assert.IsType<CilOperand.MethodInstance>(body.Instructions[^2].Operand).Value);
        Assert.Equal(CilOperation.Return, body.Instructions[^1].Operation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldReturnsAnAliasAndSkipsStaticOwner(bool isStatic)
    {
        var accessor = Method("Field", true, 1);
        var definition = new FieldDefinitionModel(new(new("Test"), 0x04000001),
            accessor.Definition.DeclaringType, "Value", CliValueKind.I4, isStatic);
        var target = new FieldInstanceModel(definition, accessor.DeclaringType, definition.SignatureType);
        var body = Builder().Build(accessor, new UnsafeAccessorBinding.Field(target));

        AssertBody(body, accessor, 1);
        Assert.Equal(isStatic ? 2 : 3, body.Instructions.Length);
        if (!isStatic)
        {
            Assert.Equal(CilOperation.LoadArgument, body.Instructions[0].Operation);
            Assert.Equal(0, Assert.IsType<CilOperand.Index>(body.Instructions[0].Operand).Value);
        }

        Assert.Equal(isStatic ? CilOperation.LoadStaticFieldAddress : CilOperation.LoadFieldAddress,
            body.Instructions[^2].Operation);
        Assert.Same(target, Assert.IsType<CilOperand.FieldInstance>(body.Instructions[^2].Operand).Value);
        Assert.Equal(CilOperation.Return, body.Instructions[^1].Operation);
    }

    [Fact]
    public void BindingFailureConstructsAndThrowsManagedExceptionWithoutReadingArguments()
    {
        var accessor = Method("Bad", true, 2);
        var exception = Method(".ctor", false, 0);
        var body = Builder().Build(accessor, new UnsafeAccessorBinding.Failure(exception));

        AssertBody(body, accessor, 1);
        Assert.Collection(body.Instructions,
            instruction =>
            {
                Assert.Equal(CilOperation.NewObject, instruction.Operation);
                Assert.Same(exception, Assert.IsType<CilOperand.MethodInstance>(instruction.Operand).Value);
            },
            instruction =>
            {
                Assert.Equal(CilOperation.Throw, instruction.Operation);
                Assert.IsType<CilOperand.None>(instruction.Operand);
            });
    }

    // An UnsafeAccessorTypeAttribute argument is declared object, so it is cast to the type the target takes.
    [Theory]
    [InlineData("constructor")]
    [InlineData("static-method")]
    [InlineData("instance-method")]
    [InlineData("instance-field")]
    public void TranslatedArgumentsAreCastToTheTargetsTypes(string shape)
    {
        var target = Method(shape == "constructor" ? ".ctor" : "Target", shape == "static-method", 2);
        var translated = shape is "constructor" or "static-method" ? 1 : 0;
        var names = new string?[translated + 2];
        names[translated + 1] = "Test.Owner, Test";
        var accessor = Translated(Method("Access", true, shape switch { "constructor" => 2, "instance-field" => 1, _ => 3 }), names);
        var field = new FieldInstanceModel(new FieldDefinitionModel(new(new("Test"), 0x04000001),
            accessor.Definition.DeclaringType, "Value", CliValueKind.I4, false), target.DeclaringType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        UnsafeAccessorBinding binding = shape switch
        {
            "constructor" => new UnsafeAccessorBinding.Constructor(target),
            "instance-field" => new UnsafeAccessorBinding.Field(field),
            _ => new UnsafeAccessorBinding.Method(target),
        };

        var body = Builder().Build(accessor, binding);

        var load = body.Instructions.Single(instruction => instruction.Operation == CilOperation.LoadArgument &&
            ((CilOperand.Index)instruction.Operand).Value == translated);
        var cast = body.Instructions[load.Offset + 1];
        Assert.Equal(CilOperation.CastClass, cast.Operation);
        var expected = shape switch
        {
            "constructor" => target.Signature.ParameterSignatureTypes[1],
            "static-method" => target.Signature.ParameterSignatureTypes[0],
            _ => target.DeclaringType,
        };
        Assert.Equal(expected, Assert.IsType<CilOperand.TypeIdentity>(cast.Operand).Value);
        Assert.Single(body.Instructions, instruction => instruction.Operation == CilOperation.CastClass);
    }

    [Fact]
    public void MissingInputsFailAtTheBuilderBoundary()
    {
        var accessor = Method("Accessor", true, 0);
        var binding = new UnsafeAccessorBinding.Constructor(accessor);
        Assert.Throws<ArgumentNullException>(() => Builder().Build(null!, binding));
        Assert.Throws<ArgumentNullException>(() => Builder().Build(accessor, null!));
    }

    private static IUnsafeAccessorBodyBuilder Builder() => Assert.IsAssignableFrom<IUnsafeAccessorBodyBuilder>(new UnsafeAccessorBodyBuilder());

    private static MethodInstanceModel Method(string name, bool isStatic, int parameterCount)
    {
        var signature = new MethodSignatureModel(CliValueKind.I4,
            [.. Enumerable.Repeat(CliValueKind.I4, parameterCount)]);
        var definition = new MethodDefinitionModel(new(new("Test"), 0x06000001),
            new(new("Test"), 0x02000001), name, isStatic, signature, 0);
        return new MethodInstanceModel(definition,
            CliTypeIdentity.Named(new("Test"), "Test", "Owner", false), [], signature);
    }

    private static MethodInstanceModel Translated(MethodInstanceModel method, string?[] names) =>
        method with
        {
            Definition = method.Definition with
            {
                UnsafeAccessor = new(1, null, false, false, new([.. names])),
            },
        };

    private static void AssertBody(CilMethodBody body, MethodInstanceModel accessor, int maxStack)
    {
        Assert.Same(accessor.Definition, body.Method);
        Assert.Same(accessor, body.MethodInstance);
        Assert.Equal(maxStack, body.MaxStack);
        Assert.Empty(body.Locals);
        Assert.Empty(body.LocalSignatureTypes);
        Assert.Empty(body.ExceptionRegions);
        Assert.Equal(Enumerable.Range(0, body.Instructions.Length), body.Instructions.Select(instruction => instruction.Offset));
        Assert.Equal(Enumerable.Range(1, body.Instructions.Length), body.Instructions.Select(instruction => instruction.NextOffset));
        Assert.All(body.Instructions, instruction => Assert.Null(instruction.OriginalOffset));
    }
}
