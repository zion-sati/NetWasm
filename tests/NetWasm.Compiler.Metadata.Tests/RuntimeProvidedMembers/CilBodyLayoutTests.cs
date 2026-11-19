// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeProvidedMembers;

using static CilBodyElements;

public sealed class CilBodyLayoutTests
{
    private static readonly AssemblyIdentity Assembly = new("Stubs");

    // A local the draft does not declare is numbered where it is first used, after the declared ones.
    [Fact]
    public void NumbersAnUndeclaredLocalWhereItIsFirstUsed()
    {
        var declared = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var used = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I8));
        var draft = new CilBodyDraft([declared],
            [Op(CilOperation.LoadInt64, new CilOperand.ConstantI8(1)), Store(used), Load(used), Op(CilOperation.Pop), Op(CilOperation.Return)],
            []);

        var body = new CilBodyLayout().Lay(draft, Method(), 1);

        Assert.Equal([CliValueKind.I4, CliValueKind.I8], body.Locals);
        Assert.Equal(new CilOperand.Index(1), body.Instructions[1].Operand);
    }

    // A label is marked once, and a branch targets a marked label.
    [Fact]
    public void RejectsALabelMarkedTwiceAndABranchToAnUnmarkedLabel()
    {
        var label = new CilBodyLabel();
        var twice = new CilBodyDraft([], [Mark(label), Op(CilOperation.Nop), Mark(label), Op(CilOperation.Return)], []);
        var unmarked = new CilBodyDraft([], [Branch(CilOperation.Branch, new CilBodyLabel()), Op(CilOperation.Return)], []);

        Assert.Equal("A label is marked once.",
            Assert.Throws<InvalidOperationException>(() => new CilBodyLayout().Lay(twice, Method(), 0)).Message);
        Assert.Equal("A branch targets a label that is never marked.",
            Assert.Throws<InvalidOperationException>(() => new CilBodyLayout().Lay(unmarked, Method(), 0)).Message);
    }

    private static MethodInstanceModel Method()
    {
        var owner = new EntityKey(Assembly, 0x02000001);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), owner, "Stub", true, new(CliValueKind.Void, []), 0);
        return new(definition, CliTypeIdentity.Named(Assembly, "Stubs", "Holder", false), [], definition.Signature);
    }
}
