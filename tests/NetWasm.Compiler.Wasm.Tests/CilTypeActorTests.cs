using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CilTypeActorTests
{
    [Fact]
    public void ResolvesStaticAndInstanceArgumentShapes()
    {
        var program = new FakeProgram();
        var argumentTypes = CreateArgumentTypes(program);
        var argumentSignatureTypes = CreateArgumentSignatureTypes(program);
        var staticBody = new StructuredMethodHeader(
            program.GetMethod(EntryKey), null, 1, [], [], []);
        var instanceBody = new StructuredMethodHeader(
            program.GetMethod(ConstructorKey), null, 1, [], [], []);

        Assert.Equal(CliValueKind.I4, argumentTypes.Resolve(staticBody, 0));
        Assert.Equal(
            CliValueKind.I4,
            argumentSignatureTypes.Resolve(staticBody, 0).StackKind);
        Assert.Equal(
            CliValueKind.ManagedReference,
            argumentTypes.Resolve(instanceBody, 0));
        Assert.Equal(
            "[Test]Test.Type",
            argumentSignatureTypes.Resolve(instanceBody, 0).CanonicalName);
    }

    [Fact]
    public void ResolvesStructuralAndMetadataTypeOperands()
    {
        var resolver = CreateTypeOperands(new FakeProgram());
        var structural = CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(
            CliValueKind.I4));

        Assert.Equal(structural, resolver.Resolve(I(
            0,
            CilOperation.DefaultValue,
            new CilOperand.TypeIdentity(structural)), methodInstance: null));
        Assert.Equal(
            "[Test]Test.Type",
            resolver.Resolve(I(
                0,
                CilOperation.DefaultValue,
                new CilOperand.Entity(TypeKey)), methodInstance: null).CanonicalName);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(I(
            0,
            CilOperation.DefaultValue), methodInstance: null));
    }

    [Fact]
    public void ResolvesValueTypeReceiversAndInstanceParametersThroughItsContract()
    {
        var assembly = new AssemblyIdentity("Test");
        var declaringTypeKey = new EntityKey(assembly, 0x02000010);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000010),
            declaringTypeKey,
            "Instance",
            false,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I8),
            1);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Test",
            "Value",
            isValueType: true);
        var instance = new MethodInstanceModel(
            method,
            declaringType,
            [],
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.F4));
        var body = Header(new CilMethodBody(method, 1, [], [])) with
        {
            MethodInstance = instance,
        };
        var resolver = ThroughContract(new ArgumentTypeResolver(
            new FixedTypeRepository(declaringTypeKey, isValueType: true)));

        Assert.Equal(CliValueKind.ManagedAddress, resolver.Resolve(body, 0));
        Assert.Equal(CliValueKind.F4, resolver.Resolve(body, 1));

        var definitionBody = Header(new CilMethodBody(method, 1, [], []));
        Assert.Equal(
            CliValueKind.ManagedAddress,
            resolver.Resolve(definitionBody, 0));
        Assert.Equal(CliValueKind.I8, resolver.Resolve(definitionBody, 1));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, 0));
    }

    private static IArgumentTypeResolver ThroughContract(
        IArgumentTypeResolver resolver) => resolver;

    private sealed class FixedTypeRepository(EntityKey key, bool isValueType) :
        ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey requestedKey)
        {
            Assert.Equal(key, requestedKey);
            return new TypeDefinitionModel(
                key,
                "Test",
                "Value",
                isValueType,
                [],
                []);
        }
    }
}
