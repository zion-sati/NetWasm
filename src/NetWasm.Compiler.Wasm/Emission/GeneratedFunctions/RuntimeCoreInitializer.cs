// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RuntimeCoreInitializer(
    ITypeDescriptorSource descriptors,
    IStaticDataLayout staticData,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses) : IRuntimeCoreInitializer
{
    public void Initialize(GeneratedFunctionWriterLease code, int staticDataEnd)
    {
        var typeCapacity = descriptors.TypeDescriptors
            .Select(descriptor => descriptor.TypeId)
            .Concat(descriptors.ConstructedTypeDescriptors.Select(
                descriptor => descriptor.TypeId))
            .Concat(descriptors.ValueTypeDescriptors.Select(
                descriptor => descriptor.TypeId))
            .Concat(descriptors.MetadataTypeDescriptors.Select(
                descriptor => descriptor.TypeId))
            .DefaultIfEmpty(0)
            .Max() + 1;
        addresses.Emit(code.Instructions, staticDataEnd);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(typeCapacity)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(staticData.StaticRootAddresses.Length)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.Initialize))));
    }
}
