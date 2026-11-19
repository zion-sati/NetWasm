// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeArchiveFunctionTypeReader
{
    // The Wasm type of each function a native archive's object files define and other objects can link to, by name.
    ImmutableDictionary<string, NativeFunctionType> Read(string archivePath);
}
