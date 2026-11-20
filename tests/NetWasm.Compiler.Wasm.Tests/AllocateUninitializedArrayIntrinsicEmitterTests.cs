// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AllocateUninitializedArrayIntrinsicEmitterTests
{
    // Only an element type without references skips the collector's clear. GC.AllocateUninitializedArray allocates the
    // others with newarr before it reaches the intrinsic, but the intrinsic still allocates them as newarr would.
    [Theory]
    [InlineData("reference", nameof(RuntimeImportSymbol.AllocateReferenceArray))]
    [InlineData("references", nameof(RuntimeImportSymbol.AllocateValueArray))]
    [InlineData("by-reference", nameof(RuntimeImportSymbol.AllocateValueArray))]
    [InlineData("plain", nameof(RuntimeImportSymbol.AllocateUninitializedValueArray))]
    public void ClearsEveryElementTypeTheCollectorTraces(string element, string expected)
    {
        var elementType = element == "reference"
            ? CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false)
            : CliTypeIdentity.Named(new("Values"), "Values", element, true);
        var layout = new ValueLayout(elementType, 8, 4, element == "references" ? [0] : [])
        {
            ByReferenceOffsets = element == "by-reference" ? [4] : [],
        };
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = new AllocateUninitializedArrayIntrinsicEmitter(layouts, new Values(layout), imports,
            CreateAddressInstructions(layouts), new ImplicitExceptionEmitter(layouts));
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.AllocateUninitializedArray, [CliValueKind.I4],
            [elementType]);
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var allocation = (uint)imports.Resolve(Enum.Parse<RuntimeImportSymbol>(expected), request.Instruction.Target.RuntimeImportSelection);
        Assert.Contains(writer.ToInstructions(), instruction => instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == allocation);
    }

    private sealed class Values(ValueLayout layout) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) => layout;
    }
}
