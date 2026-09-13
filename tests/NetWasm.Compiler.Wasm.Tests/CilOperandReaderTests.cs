using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CilOperandReaderTests
{
    [Fact]
    public void ReadsIndexAndEntityOperands()
    {
        var index = I(0, CilOperation.LoadLocal, new CilOperand.Index(7));
        var entity = I(0, CilOperation.LoadField, new CilOperand.Entity(EntryKey));

        Assert.Equal(7, CilOperandReader.GetIndex(index));
        Assert.Equal(EntryKey, CilOperandReader.GetEntity(entity));
    }

    [Fact]
    public void RejectsAnOperandOfTheWrongKind()
    {
        var instruction = I(0, CilOperation.LoadLocal, new CilOperand.None());

        Assert.Throws<InvalidCastException>(() => CilOperandReader.GetIndex(instruction));
        Assert.Throws<InvalidCastException>(() => CilOperandReader.GetEntity(instruction));
    }
}
