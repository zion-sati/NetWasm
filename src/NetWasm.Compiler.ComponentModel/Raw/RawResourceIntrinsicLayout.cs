using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawResourceIntrinsicLayout(
    WasmTarget Target,
    RawWitImportDeclaration.Resource Declaration,
    RawCanonicalImportIdentity Identity,
    CanonicalAbiCoreSignature Signature);
