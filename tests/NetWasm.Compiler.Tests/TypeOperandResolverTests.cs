// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class TypeOperandResolverTests
{
    [Fact]
    public void ResolveSubstitutesClosedMethodParametersThroughItsContract()
    {
        var expected = CreateReferenceType("MethodArgument");
        var method = CreateMethodInstance(CreateReferenceType("DeclaringType"), [expected]);
        var instruction = CreateInstruction(
            new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: true, index: 0)));
        var resolver = ThroughContract(new TypeOperandResolver(new FixedTypeIdentityResolver(expected)));

        var actual = resolver.Resolve(instruction, method);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveSubstitutesClosedDeclaringTypeParametersThroughItsContract()
    {
        var expected = CreateReferenceType("TypeArgument");
        var declaringType = CliTypeIdentity.GenericInstantiation(
            CreateReferenceType("GenericDeclaringType"),
            [expected]);
        var method = CreateMethodInstance(declaringType, []);
        var instruction = CreateInstruction(
            new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: false, index: 0)));
        var resolver = ThroughContract(new TypeOperandResolver(new FixedTypeIdentityResolver(expected)));

        var actual = resolver.Resolve(instruction, method);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveSubstitutesNestedEntityOperands()
    {
        var expectedElement = CreateReferenceType("Element");
        var unresolved = CliTypeIdentity.SzArray(CliTypeIdentity.GenericParameter(method: true, index: 0));
        var identityResolver = new FixedTypeIdentityResolver(unresolved);
        var key = new EntityKey(new AssemblyIdentity("OperandTests"), 7);
        var instruction = CreateInstruction(new CilOperand.Entity(key));
        var method = CreateMethodInstance(CreateReferenceType("DeclaringType"), [expectedElement]);
        var resolver = ThroughContract(new TypeOperandResolver(identityResolver));

        var actual = resolver.Resolve(instruction, method);

        Assert.Equal(CliTypeIdentity.SzArray(expectedElement), actual);
        Assert.Equal(key, identityResolver.LastKey);
        Assert.Equal(1, identityResolver.CallCount);
    }

    [Fact]
    public void ResolveRetainsConcreteTypeOperandsWithoutAConstructedMethod()
    {
        var expected = CreateReferenceType("Concrete");
        var instruction = CreateInstruction(new CilOperand.TypeIdentity(expected));
        var resolver = ThroughContract(new TypeOperandResolver(new FixedTypeIdentityResolver(expected)));

        var actual = resolver.Resolve(instruction, methodInstance: null);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveRejectsAnOpenGenericOperandBeforeAnalysis()
    {
        var instruction = CreateInstruction(
            new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: true, index: 0)));
        var resolver = ThroughContract(new TypeOperandResolver(
            new FixedTypeIdentityResolver(CreateReferenceType("Unused"))));

        var exception = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(instruction, methodInstance: null));
        Assert.Contains("remained open", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRejectsAnOperandThatDoesNotRepresentAType()
    {
        var identityResolver = new FixedTypeIdentityResolver(CreateReferenceType("Unused"));
        var resolver = ThroughContract(new TypeOperandResolver(identityResolver));

        Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(CreateInstruction(new CilOperand.ConstantI4(1)), methodInstance: null));
        Assert.Equal(0, identityResolver.CallCount);
    }

    private static CilInstruction CreateInstruction(CilOperand operand) =>
        new(0, 1, CilOperation.DefaultValue, operand);

    private static MethodInstanceModel CreateMethodInstance(
        CliTypeIdentity declaringType,
        ImmutableArray<CliTypeIdentity> methodArguments)
    {
        var assembly = new AssemblyIdentity("OperandTests");
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 2),
            "Cast",
            true,
            new MethodSignatureModel(CliValueKind.ManagedReference, []),
            0);
        return new(definition, declaringType, methodArguments, definition.Signature);
    }

    private static CliTypeIdentity CreateReferenceType(string name) =>
        CliTypeIdentity.Named(
            new AssemblyIdentity("OperandTests"),
            "OperandTests",
            name,
            isValueType: false,
            CliValueKind.ManagedReference);

    private static ITypeOperandResolver ThroughContract(ITypeOperandResolver resolver) => resolver;

    private sealed class FixedTypeIdentityResolver(CliTypeIdentity identity) : ITypeIdentityResolver
    {
        public int CallCount { get; private set; }

        public EntityKey? LastKey { get; private set; }

        public CliTypeIdentity GetTypeIdentity(EntityKey type)
        {
            CallCount++;
            LastKey = type;
            return identity;
        }
    }
}
