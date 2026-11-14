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
        if (request.Method.Definition.NativeImport is not null)
            return CallEmissionKind.Native;
        if (types.IsDelegateType(request.Method.Definition.DeclaringType) &&
            request.Method.Definition.Name == "Invoke")
        {
            return CallEmissionKind.DelegateInvoke;
        }

        // An intrinsic expands in place, unless what it does depends on its receiver's exact type
        // and the site dispatches on the receiver: then each receiver's case expands it.
        var instruction = request.Instruction;
        var caller = instruction.Header.MethodInstance?.CanonicalName;
        var isIntrinsic = intrinsics.TryGetIntrinsic(request.Method.Definition.Key, out var intrinsic);
        if (instruction.Instruction.Operation is CilOperation.CallVirtual or CilOperation.Call &&
            (!isIntrinsic || RuntimeIntrinsicKinds.DependsOnReceiverType(intrinsic)) &&
            caller is not null &&
            instruction.Target.DispatchCallSites.TryGetValue(
                $"{caller}@{instruction.Instruction.Offset:x8}", out var dispatch))
        {
            return CallsSharedBody(request, dispatch)
                ? CallEmissionKind.SharedObjectDispatch
                : CallEmissionKind.VirtualDispatch;
        }

        if (isIntrinsic)
        {
            return CallEmissionKind.RuntimeIntrinsic;
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

    // A virtual call on object with many targets calls CoreLib's shared body for it, which carries the one copy of
    // the dispatch. A call constrained to a reference type is an ordinary virtual call on the receiver it loads; one
    // constrained to a value type dispatches on that type.
    private static bool CallsSharedBody(CallEmissionRequest request, DispatchCallSiteModel dispatch) =>
        dispatch.SharesObjectDispatch &&
        request.Instruction.Instruction.Operation == CilOperation.CallVirtual &&
        request.ConstrainedType is not { IsValueType: true } &&
        request.Instruction.Target.SharedObjectDispatch.ContainsKey(dispatch.Declaration.CanonicalName);
}
