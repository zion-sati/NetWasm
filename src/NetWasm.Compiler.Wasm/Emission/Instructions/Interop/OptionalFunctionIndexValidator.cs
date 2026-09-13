using System;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class OptionalFunctionIndexValidator :
    IOptionalFunctionIndexValidator
{
    public void Validate(OptionalFunctionIndex index, string message)
    {
        if (!index.IsPresent)
        {
            throw new InvalidOperationException(message);
        }
    }
}
