// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// An object reference or managed address an instruction reads or writes through throws NullReferenceException when
// null, unless the body's proof says it cannot be; values and unmanaged pointers are not checked.
public sealed class NullDereferenceCheckEmitterTests
{
    [Theory]
    [InlineData(CliValueKind.ManagedReference, true)]
    [InlineData(CliValueKind.ManagedAddress, true)]
    [InlineData(CliValueKind.NativeInt, false)]
    [InlineData(CliValueKind.ValueType, false)]
    [InlineData(CliValueKind.I4, false)]
    public void ObjectsAndManagedAddressesAreChecked(CliValueKind kind, bool checkedAddress)
    {
        var request = CreateInstructionRequest(CilOperation.LoadObject, [kind]);
        var exceptions = new RecordingExceptions();

        new NullDereferenceCheckEmitter(exceptions, new AddressInstructionEmitter(new RecordingLayoutProvider()))
            .Emit(request, GetCodeWriter(request), 7, kind);

        var opcodes = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions().Select(item => item.Opcode).ToArray();
        if (checkedAddress)
        {
            Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.I32EqualZero, WasmOpcodes.If, WasmOpcodes.End], opcodes);
            Assert.Equal(7u, ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions()[0].Operand.UnsignedValue);
            Assert.Equal([ManagedExceptionKind.NullReference], exceptions.Kinds);
        }
        else
        {
            Assert.Empty(opcodes);
            Assert.Empty(exceptions.Kinds);
        }
    }

    [Theory]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.ManagedAddress)]
    public void AProvenDereferenceIsNotChecked(CliValueKind kind)
    {
        var request = CreateInstructionRequest(CilOperation.LoadObject, [kind]);
        var header = request.Header with { Instructions = [request.Instruction] };
        request = request with
        {
            Header = header with
            {
                ArrayBoundsProof = new(header.Instructions, header.Locals, header.LocalSignatureTypes, [])
                {
                    NonNullOffsets = [request.Instruction.Offset],
                },
            },
        };
        var exceptions = new RecordingExceptions();
        var code = new RecordingInstructionWriter();

        new NullDereferenceCheckEmitter(exceptions, new AddressInstructionEmitter(new RecordingLayoutProvider()))
            .Emit(request, code, 7, kind);

        Assert.Empty(code.ToInstructions());
        Assert.Empty(exceptions.Kinds);
    }

    private sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            Kinds.Add(kind);
    }
}
