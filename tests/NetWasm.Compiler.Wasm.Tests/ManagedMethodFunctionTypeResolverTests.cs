using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedMethodFunctionTypeResolverTests
{
    [Fact]
    public void ResolvesDefinitionAndConstructedInstanceTypesThroughItsContract()
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(EntryKey);
        var instance = new MethodInstanceModel(
            program.GetMethod(ConstructorKey),
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            program.GetMethod(ConstructorKey).Signature);

        var definitionType = ResolveDefinition(
            new ManagedMethodFunctionTypeResolver(),
            definition);
        var instanceType = ResolveInstance(
            new ManagedMethodFunctionTypeResolver(),
            instance);

        Assert.True(definitionType.Parameters.SequenceEqual([CliValueKind.I4]));
        Assert.Equal(CliValueKind.I4, definitionType.Result);
        Assert.True(instanceType.Parameters.SequenceEqual([CliValueKind.ManagedReference]));
        Assert.Equal(CliValueKind.Void, instanceType.Result);
    }

    [Fact]
    public void UsesManagedAddressReturnParameterForDefinitionValueTypes()
    {
        var program = new FakeProgram();
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Result",
            isValueType: true);
        var method = program.GetMethod(EntryKey) with
        {
            Signature = new MethodSignatureModel(
                valueType,
                [CliTypeIdentity.FromStackKind(CliValueKind.ValueType)]),
        };

        var result = ResolveDefinition(
            new ManagedMethodFunctionTypeResolver(),
            method);

        Assert.True(result.Parameters.SequenceEqual(
            [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress]));
        Assert.Equal(CliValueKind.Void, result.Result);
    }

    [Fact]
    public void ResolvesStaticAndValueTypeInstanceReceivers()
    {
        var program = new FakeProgram();
        var resolver = new ManagedMethodFunctionTypeResolver();
        var staticInstance = new MethodInstanceModel(
            program.GetMethod(EntryKey),
            CliTypeIdentity.Named(Assembly, "Test", "StaticType", isValueType: false),
            [],
            program.GetMethod(EntryKey).Signature);
        var valueTypeInstance = new MethodInstanceModel(
            program.GetMethod(ConstructorKey),
            CliTypeIdentity.Named(Assembly, "Test", "ValueType", isValueType: true),
            [],
            program.GetMethod(ConstructorKey).Signature);

        var staticResult = resolver.Resolve(staticInstance);
        var valueTypeResult = resolver.Resolve(valueTypeInstance);

        Assert.True(staticResult.Parameters.SequenceEqual([CliValueKind.I4]));
        Assert.True(valueTypeResult.Parameters.SequenceEqual(
            [CliValueKind.ManagedAddress]));
    }

    [Fact]
    public void UsesManagedAddressReturnParameterForConstructedValueTypes()
    {
        var program = new FakeProgram();
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Result",
            isValueType: true);
        var instance = new MethodInstanceModel(
            program.GetMethod(ConstructorKey),
            CliTypeIdentity.Named(Assembly, "Test", "Owner", isValueType: false),
            [],
            new MethodSignatureModel(valueType, []));

        var result = ResolveInstance(
            new ManagedMethodFunctionTypeResolver(),
            instance);

        Assert.True(result.Parameters.SequenceEqual(
            [CliValueKind.ManagedAddress, CliValueKind.ManagedReference]));
        Assert.Equal(CliValueKind.Void, result.Result);
    }

    private delegate WasmFunctionType DefinitionResolver(
        IManagedMethodFunctionTypeResolver resolver,
        MethodDefinitionModel method);

    private delegate WasmFunctionType InstanceResolver(
        IManagedMethodFunctionTypeResolver resolver,
        MethodInstanceModel method);

    private static readonly DefinitionResolver ResolveDefinition =
        static (resolver, method) => resolver.Resolve(method);

    private static readonly InstanceResolver ResolveInstance =
        static (resolver, method) => resolver.Resolve(method);
}
