// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class GcGetMetricIntrinsicEmitterTests
{
    [Fact]
    public void EmitReadsMetricAndPublishesInt64Result()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(RuntimeIntrinsic.GcGetMetric, runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.GcGetMetric,
            [CliValueKind.I4]);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet],
            writer.Instructions.Select(instruction => instruction.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.I4), writer.Instructions[0].Operand!.UnsignedValue);
        Assert.Equal(
            (uint)runtimeImports.Resolve(RuntimeImportSymbol.GcGetMetric),
            writer.Instructions[1].Operand!.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.I8), writer.Instructions[2].Operand!.UnsignedValue);
    }

    [Fact]
    public void EmitQueriesMetricSupportAndPublishesBooleanResult()
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = RuntimeIntrinsicEmitterTestFactory.Create(
            RuntimeIntrinsic.GcMetricIsSupported,
            runtimeImports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.GcMetricIsSupported,
            [CliValueKind.I4]);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet],
            writer.Instructions.Select(instruction => instruction.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.I4), writer.Instructions[0].Operand!.UnsignedValue);
        Assert.Equal(
            (uint)runtimeImports.Resolve(RuntimeImportSymbol.GcMetricIsSupported),
            writer.Instructions[1].Operand!.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.I4), writer.Instructions[2].Operand!.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        public List<WasmInstruction> Instructions => _instructions;

        public void Write(WasmInstruction instruction) => _instructions.Add(instruction);
    }
}

internal static class RuntimeIntrinsicEmitterTestFactory
{
    internal static IRuntimeIntrinsicEmitter Create(
        RuntimeIntrinsic intrinsic,
        IRuntimeImportResolver runtimeImports) =>
        intrinsic switch
        {
            RuntimeIntrinsic.GcGetMetric => new GcGetMetricIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.GcMetricIsSupported =>
                new GcMetricIsSupportedIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.GcWaitForPendingFinalizers =>
                new GcWaitForPendingFinalizersIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.ObjectIdentityHash => new ObjectIdentityHashIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.BoxedValueClone => new BoxedValueCloneIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.RuntimeHelpersEquals =>
                new RuntimeHelpersEqualsIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.RandomSeed => new RandomSeedIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.NativeMemoryClear => new NativeMemoryClearIntrinsicEmitter(),
            RuntimeIntrinsic.NativeMemoryCopy => new NativeMemoryCopyIntrinsicEmitter(),
            RuntimeIntrinsic.NativeMemoryFill => new NativeMemoryFillIntrinsicEmitter(),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };
}
