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

internal interface IMemberObjectAllocator
{
    // Allocates an object of the type into the local, throwing OutOfMemoryException when that fails. A rooted
    // allocation, which managed code follows, is published as the call's allocation root first, as newobj's is.
    void Allocate(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, CliTypeIdentity type,
        int destinationLocal, bool rooted);
}
