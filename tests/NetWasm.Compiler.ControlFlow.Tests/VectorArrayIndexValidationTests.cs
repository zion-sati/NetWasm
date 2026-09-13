using NetWasm.Compiler.Core;

using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class VectorArrayIndexValidationTests
{
    [Fact]
    public void ValidatorAcceptsNativeIntegerAcrossEveryVectorArrayIndexConsumer()
    {
        var i4 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var elementType = new CilOperand.TypeIdentity(i4);
        var body = Body(
            CliValueKind.Void,
            4,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.ConvertNativeInt),
            I(3, CilOperation.LoadArrayElementReference),
            I(4, CilOperation.Pop),
            I(5, CilOperation.LoadNull),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(7, CilOperation.ConvertNativeInt),
            I(8, CilOperation.LoadNull),
            I(9, CilOperation.StoreArrayElementReference),
            I(10, CilOperation.LoadNull),
            I(11, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(12, CilOperation.ConvertNativeInt),
            I(13, CilOperation.LoadArrayElement, elementType),
            I(14, CilOperation.Pop),
            I(15, CilOperation.LoadNull),
            I(16, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(17, CilOperation.ConvertNativeInt),
            I(18, CilOperation.LoadArrayElementAddress, elementType),
            I(19, CilOperation.Pop),
            I(20, CilOperation.LoadNull),
            I(21, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(22, CilOperation.ConvertNativeInt),
            I(23, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(24, CilOperation.StoreArrayElement, elementType),
            I(25, CilOperation.Return));

        var graph = Validate(body);

        Assert.Equal(CliValueKind.NativeInt, graph.InstructionEntryStacks[3][^1]);
        Assert.Equal(CliValueKind.NativeInt, graph.InstructionEntryStacks[9][1]);
        Assert.Equal(CliValueKind.NativeInt, graph.InstructionEntryStacks[13][^1]);
        Assert.Equal(CliValueKind.NativeInt, graph.InstructionEntryStacks[18][^1]);
        Assert.Equal(CliValueKind.NativeInt, graph.InstructionEntryStacks[24][1]);
    }

    [Fact]
    public void ValidatorRejectsInt64VectorArrayIndex()
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt64, new CilOperand.ConstantI8(0)),
            I(2, CilOperation.LoadArrayElementReference),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Return));

        Assert.Throws<CompilerException>(() => Validate(body));
    }
}
