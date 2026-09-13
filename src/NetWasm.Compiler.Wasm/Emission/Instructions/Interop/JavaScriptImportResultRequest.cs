using NetWasm.Compiler.Wasm.Encoding;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed record JavaScriptImportResultRequest(
    MethodSignatureModel Signature,
    List<CliValueKind> Stack,
    MethodEmissionContext Context,
    int ArgumentBase,
    int Consumed,
    HostCallbackDeclaration? Callback,
    InteropMarshallingTarget Target);
