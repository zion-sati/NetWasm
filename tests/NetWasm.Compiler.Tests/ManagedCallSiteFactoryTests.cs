// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedCallSiteFactoryTests
{
    private static readonly AssemblyIdentity TestAssembly = new("Tests");

    private readonly ManagedCallSiteFactory _factory = new(new ManagedMethodIdentityFactory());

    [Theory]
    [InlineData(CilOperation.Call, ManagedCallOperation.Direct)]
    [InlineData(CilOperation.CallVirtual, ManagedCallOperation.Virtual)]
    [InlineData(CilOperation.NewObject, ManagedCallOperation.Construct)]
    [InlineData(CilOperation.LoadFunction, ManagedCallOperation.LoadFunction)]
    [InlineData(CilOperation.LoadVirtualFunction, ManagedCallOperation.LoadVirtualFunction)]
    public void CreateMapsEveryManagedCallOperation(
        CilOperation cilOperation,
        ManagedCallOperation expectedOperation)
    {
        MethodInstanceModel caller = CreateMethod(1, "Caller");
        MethodInstanceModel target = CreateMethod(2, "Target");
        var instruction = new CilInstruction(11, 12, cilOperation, new CilOperand.MethodInstance(target));
        var body = new CilMethodBody(caller.Definition, 1, [], [instruction]);
        var typeOperands = new RecordingTypeOperandResolver(CreateType("Unused"));

        ManagedCallSite result = ((IManagedCallSiteFactory)_factory).Create(
            caller,
            body,
            0,
            target,
            typeOperands);

        Assert.Equal(expectedOperation, result.Operation);
        Assert.Equal(new ManagedMethodIdentity(caller.CanonicalName), result.Key.Caller);
        Assert.Equal(11, result.Key.InstructionOffset);
        Assert.Equal(new ManagedMethodIdentity(target.CanonicalName), result.TargetIdentity);
        Assert.Same(target, result.Target);
        Assert.Null(result.ConstrainedType);
        Assert.Equal(0, typeOperands.ResolveCount);
    }

    [Fact]
    public void CreateCarriesTheResolvedConstrainedType()
    {
        MethodInstanceModel caller = CreateMethod(3, "Caller");
        MethodInstanceModel target = CreateMethod(4, "Target");
        CliTypeIdentity constrainedType = CreateType("Receiver");
        var prefix = new CilInstruction(
            7,
            9,
            CilOperation.Constrained,
            new CilOperand.TypeIdentity(constrainedType));
        var call = new CilInstruction(
            9,
            10,
            CilOperation.CallVirtual,
            new CilOperand.MethodInstance(target));
        var body = new CilMethodBody(caller.Definition, 1, [], [prefix, call]);
        var typeOperands = new RecordingTypeOperandResolver(constrainedType);

        ManagedCallSite result = ((IManagedCallSiteFactory)_factory).Create(
            caller,
            body,
            1,
            target,
            typeOperands);

        Assert.Equal(constrainedType, result.ConstrainedType);
        Assert.Equal(1, typeOperands.ResolveCount);
        Assert.Equal(prefix, typeOperands.LastInstruction);
    }

    [Fact]
    public void CreateRejectsInvalidInputsAndNonCallInstructions()
    {
        MethodInstanceModel caller = CreateMethod(5, "Caller");
        MethodInstanceModel target = CreateMethod(6, "Target");
        var instruction = new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.MethodInstance(target));
        var body = new CilMethodBody(caller.Definition, 1, [], [instruction]);
        var invalidInstruction = new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None());
        var invalidBody = new CilMethodBody(caller.Definition, 1, [], [invalidInstruction]);
        ITypeOperandResolver typeOperands = new RecordingTypeOperandResolver(CreateType("Unused"));

        Assert.Throws<ArgumentNullException>(() =>
            ((IManagedCallSiteFactory)_factory).Create(null!, body, 0, target, typeOperands));
        Assert.Throws<ArgumentNullException>(() =>
            ((IManagedCallSiteFactory)_factory).Create(caller, null!, 0, target, typeOperands));
        Assert.Throws<ArgumentNullException>(() =>
            ((IManagedCallSiteFactory)_factory).Create(caller, body, 0, null!, typeOperands));
        Assert.Throws<ArgumentNullException>(() =>
            ((IManagedCallSiteFactory)_factory).Create(caller, body, 0, target, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ((IManagedCallSiteFactory)_factory).Create(caller, body, -1, target, typeOperands));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ((IManagedCallSiteFactory)_factory).Create(
                caller,
                body,
                body.Instructions.Length,
                target,
                typeOperands));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ((IManagedCallSiteFactory)_factory).Create(caller, invalidBody, 0, target, typeOperands));
    }

    private static MethodInstanceModel CreateMethod(int token, string name)
    {
        var typeKey = new EntityKey(TestAssembly, 100);
        var signature = new MethodSignatureModel(CliValueKind.Void, []);
        var definition = new MethodDefinitionModel(
            new EntityKey(TestAssembly, token),
            typeKey,
            name,
            IsStatic: true,
            signature,
            RelativeVirtualAddress: 0);

        return new MethodInstanceModel(
            definition,
            CreateType("Container"),
            ImmutableArray<CliTypeIdentity>.Empty,
            signature);
    }

    private static CliTypeIdentity CreateType(string name) =>
        CliTypeIdentity.Named(TestAssembly, "Tests", name, isValueType: false);

    private sealed class RecordingTypeOperandResolver(CliTypeIdentity result) : ITypeOperandResolver
    {
        public int ResolveCount { get; private set; }

        public CilInstruction? LastInstruction { get; private set; }

        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance)
        {
            ResolveCount++;
            LastInstruction = instruction;
            return result;
        }
    }
}
