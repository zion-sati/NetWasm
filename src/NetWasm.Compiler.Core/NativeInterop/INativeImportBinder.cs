// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeImportBinder
{
    // Whether a native import binds to a library the program links, or compiles to a throwing body.
    bool Binds(NativeImportDeclaration import);
}
