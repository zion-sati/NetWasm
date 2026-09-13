using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class FilterEnvironmentRootEmitter(
    ITargetLayout layouts,
    IValueFrameAddressEmitter valueFrameAddresses,
    IAddressInstructionEmitter addresses) :
    IFilterEnvironmentRootEmitter
{
    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        if (context.FilterEnvironment.RootSlotCount == 0)
        {
            return;
        }
        foreach (var capture in context.FilterEnvironment.Captures.Values
                     .OrderBy(capture => capture.Slot.IsArgument ? 0 : 1)
                     .ThenBy(capture => capture.Slot.Index))
        {
            foreach ((var byteOffset, var rootSlot) in capture.Roots)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)context.FilterRootFrame)));
                if (rootSlot != 0)
                {
                    addresses.Emit(code,
                        rootSlot * layouts.Target.ObjectReferenceSize);
                    addresses.Emit(code, AddressOperation.Add);
                }
                valueFrameAddresses.Emit(code, context, capture.Offset);
                ManagedMemoryEmitter.EmitLoadBySize(
                    code,
                    layouts.Target,
                    byteOffset,
                    layouts.Target.ObjectReferenceSize);
                ManagedMemoryEmitter.EmitStoreBySize(
                    code,
                    layouts.Target,
                    0,
                    layouts.Target.ObjectReferenceSize);
            }
        }
    }

}
