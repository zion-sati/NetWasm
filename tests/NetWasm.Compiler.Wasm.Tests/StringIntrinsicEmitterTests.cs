// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StringIntrinsicEmitterTests
{
    [Fact]
    public void EmitsStringLengthThroughItsCapability()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new StringLengthIntrinsicEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringLength,
            [CliValueKind.ManagedReference]);
        request = request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with
                    {
                        FunctionIndices = request.Instruction.Target.FunctionIndices with
                        {
                            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(43),
                        },
                    },
                },
            },
        };

        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        ((IRuntimeIntrinsicEmitter)emitter).Emit(request, writer);

        Assert.NotEmpty(writer.ToInstructions());
    }

    [Fact]
    public void EmitsCharacterAccessThroughItsCapability()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new StringCharacterAtIntrinsicEmitter(
            layouts,
            layouts,
            new ImplicitExceptionEmitter(layouts),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringCharacterAt,
            [CliValueKind.ManagedReference, CliValueKind.I4]);

        ((IRuntimeIntrinsicEmitter)emitter).Emit(request, EmitterTestSupport.GetCodeWriter(request.Instruction));

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }
}
