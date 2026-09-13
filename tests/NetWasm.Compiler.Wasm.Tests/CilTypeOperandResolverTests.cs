using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class CilTypeOperandResolverTests
{
    [Fact]
    public void ResolveSubstitutesClosedMethodParametersThroughItsContract()
    {
        var expected = CreateReferenceType("MethodArgument");
        var request = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.UnboxAny,
            operand: new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: true, index: 0)));
        var method = CreateMethodInstance(
            request.Header.Method!,
            CreateReferenceType("DeclaringType"),
            [expected]);
        var resolver = ThroughContract(new CilTypeOperandResolver(new FixedTypeIdentityResolver(expected)));

        var actual = resolver.Resolve(request.Instruction, method);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveSubstitutesClosedDeclaringTypeParametersThroughItsContract()
    {
        var expected = CreateReferenceType("TypeArgument");
        var request = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.UnboxAny,
            operand: new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: false, index: 0)));
        var declaringType = CliTypeIdentity.GenericInstantiation(
            CreateReferenceType("DeclaringType"),
            [expected]);
        var method = CreateMethodInstance(request.Header.Method!, declaringType, []);
        var resolver = ThroughContract(new CilTypeOperandResolver(new FixedTypeIdentityResolver(expected)));

        var actual = resolver.Resolve(request.Instruction, method);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveSubstitutesEntityOperandsReturnedByTheIdentityResolver()
    {
        var expectedElement = CreateReferenceType("Element");
        var unresolved = CliTypeIdentity.SzArray(CliTypeIdentity.GenericParameter(method: true, index: 0));
        var identityResolver = new FixedTypeIdentityResolver(unresolved);
        var key = new EntityKey(new AssemblyIdentity("OperandTests"), 1);
        var request = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.UnboxAny,
            operand: new CilOperand.Entity(key));
        var method = CreateMethodInstance(
            request.Header.Method!,
            CreateReferenceType("DeclaringType"),
            [expectedElement]);
        var resolver = ThroughContract(new CilTypeOperandResolver(identityResolver));

        var actual = resolver.Resolve(request.Instruction, method);

        Assert.Equal(CliTypeIdentity.SzArray(expectedElement), actual);
        Assert.Equal(key, identityResolver.LastKey);
        Assert.Equal(1, identityResolver.CallCount);
    }

    [Fact]
    public void ResolveRetainsConcreteTypeOperandsWithoutAConstructedMethod()
    {
        var expected = CreateReferenceType("Concrete");
        var request = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.UnboxAny,
            operand: new CilOperand.TypeIdentity(expected));
        var resolver = ThroughContract(new CilTypeOperandResolver(new FixedTypeIdentityResolver(expected)));

        var actual = resolver.Resolve(request.Instruction, methodInstance: null);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveRejectsAnOpenGenericOperandBeforeEmission()
    {
        var request = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.UnboxAny,
            operand: new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: true, index: 0)));
        var resolver = ThroughContract(new CilTypeOperandResolver(
            new FixedTypeIdentityResolver(CreateReferenceType("Unused"))));

        var exception = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(request.Instruction, methodInstance: null));
        Assert.Contains("remained open", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRejectsAnOperandThatDoesNotRepresentAType()
    {
        var identityResolver = new FixedTypeIdentityResolver(CreateReferenceType("Unused"));
        var request = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.UnboxAny,
            operand: new CilOperand.ConstantI4(1));
        var resolver = ThroughContract(new CilTypeOperandResolver(identityResolver));

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(request.Instruction, methodInstance: null));
        Assert.Equal(0, identityResolver.CallCount);
    }

    private static MethodInstanceModel CreateMethodInstance(
        MethodDefinitionModel definition,
        CliTypeIdentity declaringType,
        ImmutableArray<CliTypeIdentity> methodArguments) =>
        new(definition, declaringType, methodArguments, definition.Signature);

    private static CliTypeIdentity CreateReferenceType(string name) =>
        CliTypeIdentity.Named(
            new AssemblyIdentity("OperandTests"),
            "OperandTests",
            name,
            isValueType: false,
            CliValueKind.ManagedReference);

    private static ICilTypeOperandResolver ThroughContract(ICilTypeOperandResolver resolver) => resolver;

    private sealed class FixedTypeIdentityResolver(CliTypeIdentity identity) : ICilTypeIdentityResolver
    {
        public int CallCount { get; private set; }

        public EntityKey? LastKey { get; private set; }

        public CliTypeIdentity Resolve(EntityKey key)
        {
            CallCount++;
            LastKey = key;
            return identity;
        }
    }
}
