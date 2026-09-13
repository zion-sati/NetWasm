using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal interface IWasmEmissionInvariantValidator
{
    void Validate(ReadOnlySpan<byte> bytes, WasmTarget target);
}
