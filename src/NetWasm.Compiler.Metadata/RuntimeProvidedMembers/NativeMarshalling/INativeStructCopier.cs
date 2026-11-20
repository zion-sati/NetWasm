// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// Copies a struct between its managed value and its native copy (NativeStructLayout) in a marshalling stub's CIL.
internal interface INativeStructCopier
{
    // The bytes the native copy needs: the larger of the struct's wasm32 and wasm64 layouts (NativeStructBuffer).
    int NativeSize { get; }

    // Copies the managed struct at the address the elements push into the native copy at the buffer's address, or the
    // native copy back into the managed struct.
    ImmutableArray<CilBodyElement> Copy(ImmutableArray<CilBodyElement> managed, CilBodyLocal buffer,
        NativeCopyDirection direction);
}
