// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A field instruction naming a static field takes its static form with the object popped, before any prefix that
// marks it; one naming an instance field, and a body without either, stay as they are.
public sealed class StaticFieldInstanceAccessRewriterTests
{
    private static readonly FieldDefinitionModel Static = new(new(ValidationTestData.Assembly, 0x04000001),
        new(ValidationTestData.Assembly, 0x02000001), "S", CliValueKind.I4, IsStatic: true);
    private static readonly FieldDefinitionModel Instance = new(new(ValidationTestData.Assembly, 0x04000002),
        new(ValidationTestData.Assembly, 0x02000001), "I", CliValueKind.I4, IsStatic: false);

    [Fact]
    public void FieldInstructionsOnStaticsTakeTheirStaticFormsAndPopTheObject()
    {
        var body = Body(
            (CilOperation.LoadNull, null), (CilOperation.LoadField, Static), (CilOperation.Pop, null),
            (CilOperation.LoadNull, null), (CilOperation.Volatile, null), (CilOperation.LoadFieldAddress, Static),
            (CilOperation.Pop, null),
            (CilOperation.LoadNull, null), (CilOperation.LoadInt32, null), (CilOperation.Volatile, null),
            (CilOperation.StoreField, Static),
            (CilOperation.LoadNull, null), (CilOperation.LoadField, Instance), (CilOperation.Pop, null),
            (CilOperation.Return, null));

        var rewritten = new StaticFieldInstanceAccessRewriter(new Fields()).Rewrite(body);

        Assert.Equal(
        [
            CilOperation.LoadNull, CilOperation.Pop, CilOperation.LoadStaticField, CilOperation.Pop,
            CilOperation.LoadNull, CilOperation.Pop, CilOperation.Volatile, CilOperation.LoadStaticFieldAddress, CilOperation.Pop,
            CilOperation.LoadNull, CilOperation.LoadInt32, CilOperation.Volatile, CilOperation.StoreStaticField, CilOperation.Pop,
            CilOperation.LoadNull, CilOperation.LoadField, CilOperation.Pop,
            CilOperation.Return,
        ], rewritten.Instructions.Select(static instruction => instruction.Operation));
    }

    [Fact]
    public void ABodyWithoutThemIsReturnedAsItIs()
    {
        var body = Body((CilOperation.LoadNull, null), (CilOperation.LoadField, Instance), (CilOperation.Return, null));

        Assert.Same(body, new StaticFieldInstanceAccessRewriter(new Fields()).Rewrite(body));
        Assert.Throws<ArgumentNullException>(() => new StaticFieldInstanceAccessRewriter(null!));
        Assert.Throws<ArgumentNullException>(() => new StaticFieldInstanceAccessRewriter(new Fields()).Rewrite(null!));
    }

    private static CilMethodBody Body(params (CilOperation Operation, FieldDefinitionModel? Field)[] code) =>
        new(new MethodDefinitionModel(new(ValidationTestData.Assembly, 0x06000001), new(ValidationTestData.Assembly, 0x02000001),
                "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 1),
            4, [], [.. code.Select((step, offset) => new CilInstruction(offset, offset + 1, step.Operation,
                step.Field is { } field ? new CilOperand.Entity(field.Key)
                : step.Operation == CilOperation.LoadInt32 ? new CilOperand.ConstantI4(3) : new CilOperand.None()))]);

    private sealed class Fields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => key == Static.Key ? Static : Instance;
    }
}
