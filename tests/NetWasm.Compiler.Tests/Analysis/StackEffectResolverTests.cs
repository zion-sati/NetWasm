// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// A call pops its arguments (and its receiver) and pushes its result; a newobj pops its arguments and pushes the
// object; a call whose method does not resolve is not followed; anything else moves what its opcode says.
public sealed class StackEffectResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Effects");

    [Theory]
    [InlineData(CilOperation.Call, true, true, 2, 1)]
    [InlineData(CilOperation.CallVirtual, false, true, 3, 1)]
    [InlineData(CilOperation.Call, true, false, 2, 0)]
    [InlineData(CilOperation.NewObject, false, false, 2, 1)]
    public void ACallMovesItsArgumentsAndResult(CilOperation operation, bool isStatic, bool returns, int pops, int pushes)
    {
        var signature = MethodSignatureModel.Create(returns ? CliValueKind.I4 : CliValueKind.Void, CliValueKind.I4, CliValueKind.I8);
        var method = new MethodInstanceModel(new MethodDefinitionModel(new EntityKey(Assembly, 0x06000001),
            new EntityKey(Assembly, 0x02000001), "M", isStatic, signature, 1), CliTypeIdentity.Named(Assembly, "Effects", "Owner", false),
            [], signature);

        var effect = new StackEffectResolver(new Calls()).Resolve(new CilInstruction(0, 5, operation, new CilOperand.MethodInstance(method)));

        Assert.Equal((pops, pushes), effect!.Value);
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.NewObject)]
    public void ACallWhoseMethodDoesNotResolveIsNotFollowed(CilOperation operation) =>
        Assert.Null(new StackEffectResolver(new Calls()).Resolve(new CilInstruction(0, 5, operation, new CilOperand.None())));

    [Fact]
    public void AnyOtherInstructionMovesWhatItsOpcodeSays()
    {
        Assert.Equal((2, 1), new StackEffectResolver(new Calls()).Resolve(new CilInstruction(0, 1, CilOperation.Add, new CilOperand.None()))!.Value);
        Assert.Throws<ArgumentNullException>(() => new StackEffectResolver(null!));
    }

    private sealed class Calls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;
    }
}
