// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IManagedExecutableArgumentFactoryResolver
{
    EntityKey? Resolve(
        CompilerEntryPointKind kind,
        MethodDefinitionModel entryPoint,
        ITypeFinder types,
        IMethodRepository methods,
        IMethodBodyReader bodies);
}
