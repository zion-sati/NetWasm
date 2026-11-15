// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IInstructionDispatcherFactory
{
    ICilInstructionDispatcher Create();
}

internal sealed class InstructionDispatcherFactory(
    IEnumerable<IInstructionCommandProvider> providers,
    IRootPublicationEmitter roots,
    IStackStorageAnchorEmitter anchors) : IInstructionDispatcherFactory
{
    public ICilInstructionDispatcher Create() => new CilInstructionDispatcher(new InstructionCommandRegistry(
        providers.SelectMany(provider => provider.Commands)), roots, anchors);
}
