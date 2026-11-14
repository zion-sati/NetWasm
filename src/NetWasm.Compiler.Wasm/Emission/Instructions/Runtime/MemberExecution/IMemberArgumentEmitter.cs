using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

internal interface IMemberArgumentEmitter
{
    // Pushes the argument array's elements as the parameters take them: references as they are, boxed values
    // checked and unboxed (a struct by its box's contents).
    void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, int argumentsLocal,
        IReadOnlyList<CliTypeIdentity> parameters);
}
