// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public abstract record RawWitImportLayout(WasmTarget Target, RawCanonicalImportIdentity Identity, CanonicalAbiCoreSignature Signature)
{
    public sealed record Callable(RawWitFunctionLayout Function)
        : RawWitImportLayout(Function.Layout.Target, new(Function.Layout.Module, Function.Layout.Name), Function.Layout.Signature);

    public sealed record Resource(RawResourceIntrinsicLayout Intrinsic)
        : RawWitImportLayout(Intrinsic.Target, Intrinsic.Identity, Intrinsic.Signature);

    /// <summary>A stream or future built-in and the stream or future type it serves.</summary>
    public sealed record Payload : RawWitImportLayout
    {
        public Payload(
            WasmTarget target,
            RawCanonicalImportIdentity identity,
            CanonicalAbiCoreSignature signature,
            RawWitImportDeclaration.Payload declaration,
            CanonicalAbiType type)
            : base(target, identity, signature)
        {
            Declaration = declaration;
            Type = type;
        }

        public RawWitImportDeclaration.Payload Declaration { get; }

        public CanonicalAbiType Type { get; }
    }
}

public sealed record RawWitImportLayoutRequest(WitDocument Document, RawWitImportDeclaration Declaration, WasmTarget Target);
