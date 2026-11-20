// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

// The by-reference locals that hold a vector variable's address and become that address at each load, and the ones
// that keep their storage. VectorLocalLaneStoreCompilationTests run `ref` locals and inlined `ref` and `in`
// parameters against desktop .NET.
public sealed class ByReferenceLocalForwarderTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Vector4 = CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector4", isValueType: true);

    [Fact]
    public void ALocalHoldingAVectorLocalsAddressBecomesThatAddress()
    {
        var body = Body(Vector4, code => code.Local(CilOperation.LoadLocalAddress, 0).Local(CilOperation.StoreLocal, 1)
            .Local(CilOperation.LoadLocal, 1).Op(CilOperation.LoadFloat32, 2.0).Op(CilOperation.StoreField, Lane("Y"))
            .Local(CilOperation.LoadLocal, 1).Op(CilOperation.LoadField, Lane("X")).Op(CilOperation.Pop).Return());

        var rewritten = new ByReferenceLocalForwarder(enabled: true).Rewrite(body);

        Assert.Equal(
            [CilOperation.Nop, CilOperation.Nop, CilOperation.LoadLocalAddress, CilOperation.LoadFloat32, CilOperation.StoreField,
                CilOperation.LoadLocalAddress, CilOperation.LoadField, CilOperation.Pop, CilOperation.Return],
            rewritten.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[2].Operand);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[5].Operand);
        Assert.Equal(body.Instructions.Select(static instruction => instruction.Offset),
            rewritten.Instructions.Select(static instruction => instruction.Offset));
    }

    // An inlined call's `in` or `ref` parameter spills the caller's argument address the same way.
    [Fact]
    public void ALocalHoldingAVectorArgumentsAddressBecomesThatAddress()
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", true,
            new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), [Vector4]), 1);
        var instructions = new CilBuilder().Local(CilOperation.LoadArgumentAddress, 0).Local(CilOperation.StoreLocal, 0)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadField, Lane("Z")).Op(CilOperation.Pop).Return().Build();
        var body = new CilMethodBody(method, 8, [CliValueKind.ManagedAddress], instructions)
        {
            LocalSignatureTypes = [CliTypeIdentity.ManagedByReference(Vector4)],
        };

        var rewritten = new ByReferenceLocalForwarder(enabled: true).Rewrite(body);

        Assert.Equal(CilOperation.LoadArgumentAddress, rewritten.Instructions[2].Operation);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[2].Operand);
    }

    // Another variable's by-reference local keeps its storage, as does one stored twice, loaded before its store,
    // pinned, or itself addressed.
    [Theory]
    [InlineData("scalar")]
    [InlineData("twice")]
    [InlineData("early")]
    [InlineData("pinned")]
    [InlineData("addressed")]
    public void ALocalTheAddressCannotReplaceKeepsItsStorage(string shape)
    {
        var body = Body(shape == "scalar" ? Int32 : Vector4, code =>
        {
            if (shape == "early")
            {
                code.Local(CilOperation.LoadLocal, 1).Op(CilOperation.Pop);
            }
            code.Local(CilOperation.LoadLocalAddress, 0).Local(CilOperation.StoreLocal, 1);
            _ = shape switch
            {
                "twice" => code.Local(CilOperation.LoadLocalAddress, 0).Local(CilOperation.StoreLocal, 1),
                "addressed" => code.Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.Pop),
                _ => code,
            };
            return code.Local(CilOperation.LoadLocal, 1).Op(CilOperation.Pop).Return();
        });
        if (shape == "pinned")
        {
            body = body with { PinnedLocals = [1] };
        }

        Assert.Same(body, new ByReferenceLocalForwarder(enabled: true).Rewrite(body));
    }

    // A local a rewrite added without its signature type is not known to be a vector.
    [Fact]
    public void ALocalWithoutASignatureTypeIsNoVector()
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var instructions = new CilBuilder().Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.StoreLocal, 0)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Pop).Return().Build();
        var body = new CilMethodBody(method, 8, [CliValueKind.ManagedAddress, CliValueKind.V128], instructions)
        {
            LocalSignatureTypes = [CliTypeIdentity.ManagedByReference(Vector4)],
        };

        Assert.Same(body, new ByReferenceLocalForwarder(enabled: true).Rewrite(body));
    }

    // ldarga 0 in an instance method addresses its this, which is no parameter.
    [Fact]
    public void AnInstanceMethodsThisIsNoVectorArgument()
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", false,
            new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), [Vector4]), 1);
        var instructions = new CilBuilder().Local(CilOperation.LoadArgumentAddress, 0).Local(CilOperation.StoreLocal, 0)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Pop).Return().Build();
        var body = new CilMethodBody(method, 8, [CliValueKind.ManagedAddress], instructions)
        {
            LocalSignatureTypes = [CliTypeIdentity.ManagedByReference(Vector4)],
        };

        Assert.Same(body, new ByReferenceLocalForwarder(enabled: true).Rewrite(body));
    }

    [Fact]
    public void RejectsANullBodyAndLeavesBodiesWhenDisabled()
    {
        var body = Body(Vector4, code => code.Local(CilOperation.LoadLocalAddress, 0).Local(CilOperation.StoreLocal, 1)
            .Local(CilOperation.LoadLocal, 1).Op(CilOperation.Pop).Return());

        Assert.Throws<ArgumentNullException>(() => new ByReferenceLocalForwarder(enabled: true).Rewrite(null!));
        Assert.Same(body, new ByReferenceLocalForwarder(enabled: false).Rewrite(body));
    }

    private static CilOperand.FieldInstance Lane(string name) =>
        new(new FieldInstanceModel(
            new FieldDefinitionModel(new(Assembly, 0x04000010 + (name[0] - 'W')), new(Assembly, 0x02000010), name, CliValueKind.F4, false)
            {
                SignatureType = Single,
            },
            Vector4,
            Single));

    private static CilMethodBody Body(CliTypeIdentity variable, Func<CilBuilder, CilBuilder> write)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new(method, 8, [variable.StackKind, CliValueKind.ManagedAddress], write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [variable, CliTypeIdentity.ManagedByReference(variable)],
        };
    }
}
