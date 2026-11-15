// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StringLengthIntrinsicEmitterTests
{
    [Fact]
    public void EmitsNullFailureThroughPlannedManagedThrowFunction()
    {
        var layouts = new RecordingLayoutProvider();
        var exceptions = new RecordingExceptionEmitter();
        var request = WithThrowFunction(
            CreateRuntimeIntrinsicRequest(
                RuntimeIntrinsic.StringLength,
                [CliValueKind.ManagedReference]),
            OptionalFunctionIndex.At(43));
        var writer = new RecordingInstructionWriter();

        new StringLengthIntrinsicEmitter(
            layouts,
            exceptions,
            CreateAddressInstructions(layouts)).Emit(request, writer);

        Assert.Equal(1, exceptions.Calls);
        Assert.Equal(ManagedExceptionKind.NullReference, exceptions.Kind);
        Assert.Equal(43, exceptions.FunctionIndex.Value);
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32EqualZero,
                WasmOpcodes.If,
                WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32Load,
                WasmOpcodes.LocalSet,
            ],
            writer.ToInstructions().Select(instruction => instruction.Opcode));
    }

    private static RuntimeIntrinsicEmissionRequest WithThrowFunction(
        RuntimeIntrinsicEmissionRequest request,
        OptionalFunctionIndex functionIndex) => request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Context = request.Instruction.Context with
                    {
                        ManagedThrowFunction = functionIndex,
                    },
                },
            },
        };

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public int Calls { get; private set; }
        public ManagedExceptionKind Kind { get; private set; }
        public OptionalFunctionIndex FunctionIndex { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            ManagedExceptionKind kind,
            OptionalFunctionIndex throwFunctionIndex)
        {
            Calls++;
            Kind = kind;
            FunctionIndex = throwFunctionIndex;
        }
    }
}
