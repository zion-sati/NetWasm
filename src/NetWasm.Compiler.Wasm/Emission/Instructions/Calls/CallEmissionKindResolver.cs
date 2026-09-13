using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class CallEmissionKindResolver(
    ITypeClassifier types,
    IRuntimeIntrinsicRegistry intrinsics) : ICallEmissionKindResolver
{
    public CallEmissionKind Resolve(CallEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (types.IsDelegateType(request.Method.Definition.DeclaringType) &&
            request.Method.Definition.Name == "Invoke")
        {
            return CallEmissionKind.DelegateInvoke;
        }

        if (intrinsics.TryGetIntrinsic(request.Method.Definition.Key, out _))
        {
            return CallEmissionKind.RuntimeIntrinsic;
        }

        var instruction = request.Instruction;
        var caller = instruction.Header.MethodInstance?.CanonicalName;
        if (instruction.Instruction.Operation == CilOperation.CallVirtual &&
            caller is not null &&
            instruction.Target.DispatchCallSites.ContainsKey(
                $"{caller}@{instruction.Instruction.Offset:x8}"))
        {
            return CallEmissionKind.VirtualDispatch;
        }

        if (instruction.Target.JavaScriptAsyncBindings.ContainsKey(
                request.Method.Definition.Key))
        {
            return CallEmissionKind.AsyncJSImport;
        }

        return request.Method.Definition.JSImport is not null
            ? CallEmissionKind.JavaScriptImport
            : CallEmissionKind.Direct;
    }
}
