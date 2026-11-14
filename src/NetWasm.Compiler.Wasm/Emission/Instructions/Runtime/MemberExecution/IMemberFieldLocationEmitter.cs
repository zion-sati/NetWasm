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

internal interface IMemberFieldLocationEmitter
{
    // Pushes a static field's storage, or the receiver (an object, or a struct's box), and returns the field's
    // offset from it; with address, pushes the field's address and returns 0.
    int Emit(IWasmInstructionWriter code, int receiverLocal, FieldInstanceModel field, bool address);
}
