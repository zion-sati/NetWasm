// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class JavaScriptImportArgumentEmitterTests
{
    [Fact]
    public void ScalarArgumentLoadsTheSuppliedLocalDirectly()
    {
        var addresses = new RecordingAddressEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        CreateEmitter(new RecordingLayoutProvider(), addresses).Emit(
            code,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            7,
            new InteropImportPlan([], default, default, default, default));

        Assert.Empty(addresses.Values);
        Assert.Equal([WasmOpcodes.LocalGet, 7], code.ToArray());
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 8)]
    public void HostObjectArgumentLoadsItsHandleWithTargetHeaderWidth(
        bool memory64,
        int expectedHeaderSize)
    {
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        var addresses = new RecordingAddressEmitter();
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var type = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "System.Runtime.InteropServices.JavaScript",
            "JSObject",
            isValueType: false);

        CreateEmitter(new RecordingLayoutProvider(target), addresses).Emit(
            code,
            type,
            7,
            new InteropImportPlan([], default, default, default, default));

        Assert.Equal(
            [AddressOperation.EqualZero, expectedHeaderSize, AddressOperation.Add],
            addresses.Values);
        Assert.Contains(WasmOpcodes.If, code.ToArray());
        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
    }

    // An async import once kept its task's handle in a method local that a JSObject argument then overwrote with the
    // object's own handle, so the import completed whichever task that number named and the awaited one never did. The
    // handle is the if's value: the argument writes no local at all.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostObjectArgumentWritesNoLocal(bool memory64)
    {
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var type = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "System.Runtime.InteropServices.JavaScript",
            "JSObject",
            isValueType: false);

        CreateEmitter(new RecordingLayoutProvider(target), new RecordingAddressEmitter()).Emit(
            code,
            type,
            7,
            new InteropImportPlan([], default, default, default, default));

        var instructions = code.ToInstructions();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.LocalSet or WasmOpcodes.LocalTee);
        var conditional = Assert.Single(instructions, instruction => instruction.Opcode == WasmOpcodes.If);
        Assert.Equal((byte)WasmValueType.I32, conditional.Operand.BlockTypeValue);
    }

    private static IJavaScriptImportArgumentEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IAddressInstructionEmitter addresses) =>
        new[] { new JavaScriptImportArgumentEmitter(
                layouts, addresses, new JavaScriptNullableEmitter(layouts, layouts, addresses)) }
            .Cast<IJavaScriptImportArgumentEmitter>()
            .Single();

    private sealed class RecordingAddressEmitter : IAddressInstructionEmitter
    {
        public List<object> Values { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            Values.Add(constant);

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            Values.Add(operation);
    }
}
