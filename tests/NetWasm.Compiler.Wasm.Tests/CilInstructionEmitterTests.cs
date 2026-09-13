using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

public sealed class CilInstructionEmitterTests
{
    [Fact]
    public void CheckedArithmeticAndClassCastEmitManagedSlowPaths()
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EntryKey);
        var structured = Structure(
            program,
            entry,
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.CastClass, new CilOperand.Entity(TypeKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(5, CilOperation.AddChecked),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(7, CilOperation.SubtractChecked),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(9, CilOperation.MultiplyChecked),
            I(10, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(11, CilOperation.MultiplyCheckedUnsigned),
            I(12, CilOperation.Return));

        var module = CreateManagedWasmEmitter(
            program,
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            new WasmModuleEmitterFactory()).Emit(
                entry,
                new Dictionary<EntityKey, StructuredMethod> { [EntryKey] = structured },
                EmptyRootMaps([EntryKey]),
                [],
                ImmutableDictionary<string, EntityKey>.Empty);

        byte[] xor = [0x73];
        Assert.True(module.AsSpan().IndexOf(xor) >= 0);
        Assert.True(module.AsSpan().IndexOf("is_assignable"u8) >= 0);
        Assert.True(module.AsSpan().IndexOf("begin_throw"u8) >= 0);
    }
}
