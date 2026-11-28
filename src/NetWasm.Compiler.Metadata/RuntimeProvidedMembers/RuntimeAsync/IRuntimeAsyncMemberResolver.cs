// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// Resolves the CoreLib members the runtime async lowering calls; AsyncHelpers is the type whose awaits it replaces and
// Exception the type its fault handler catches.
internal interface IRuntimeAsyncMemberResolver
{
    EntityKey AsyncHelpers { get; }

    TypeDefinitionModel Exception { get; }

    // System.Threading.AmbientContextChanges.Made: whether the program has changed an ambient context.
    FieldInstanceModel ContextsChanged { get; }

    MethodInstanceModel Resolve(RuntimeAsyncMember member);
}
