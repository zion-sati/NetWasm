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

internal interface IMemberReferenceCheckEmitter
{
    // Throws NullReferenceException for a null reference and, given a value type, InvalidCastException unless the
    // reference is a box of exactly that type.
    void Check(IWasmInstructionWriter code, int local, CliTypeIdentity? boxedType, OptionalFunctionIndex throwFunction);
}
