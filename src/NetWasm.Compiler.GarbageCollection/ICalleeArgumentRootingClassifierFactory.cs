// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface ICalleeArgumentRootingClassifierFactory
{
    ICalleeArgumentRootingClassifier Create(
        ITypeRepository types,
        IMethodRepository methods,
        IEntryLookup<string, DispatchCallSiteModel> dispatchCallSites,
        ITypeClassifier typeClassifier);
}
