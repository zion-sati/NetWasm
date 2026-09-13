using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public abstract record RawWitImportLayout(WasmTarget Target, RawCanonicalImportIdentity Identity, CanonicalAbiCoreSignature Signature)
{
    public sealed record Callable(RawWitFunctionLayout Function)
        : RawWitImportLayout(Function.Layout.Target, new(Function.Layout.Module, Function.Layout.Name), Function.Layout.Signature);

    public sealed record Resource(RawResourceIntrinsicLayout Intrinsic)
        : RawWitImportLayout(Intrinsic.Target, Intrinsic.Identity, Intrinsic.Signature);
}

public sealed record RawWitImportLayoutRequest(WitDocument Document, RawWitImportDeclaration Declaration, WasmTarget Target);
