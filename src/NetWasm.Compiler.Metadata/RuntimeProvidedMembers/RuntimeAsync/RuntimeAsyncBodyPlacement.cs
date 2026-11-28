// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IRuntimeAsyncBodyPlacement
{
    // Whether the runtime-async method runs its lowered body in its separate body (RuntimeAsyncBodies), its entry calling
    // it and a direct await calling the body itself.
    bool Separates(MethodDefinitionModel method);
}

// A compilation that takes whole-program facts (O2, O3, Os, Oz) separates every body a method has: there
// RuntimeAsync.ContextsMayChange folds, so a direct await calls the body with no request and no entry.
internal sealed class SeparateRuntimeAsyncBodies : IRuntimeAsyncBodyPlacement
{
    public bool Separates(MethodDefinitionModel method) => method.RuntimeAsyncBody is not null;
}

// Any other (None, O0, O1) separates none: nothing folds the question, so every direct await would reach the entry
// through its request and pay the entry's call on top of what the method did before. Every method runs its own body.
internal sealed class NoSeparateRuntimeAsyncBodies : IRuntimeAsyncBodyPlacement
{
    public bool Separates(MethodDefinitionModel method) => false;
}
