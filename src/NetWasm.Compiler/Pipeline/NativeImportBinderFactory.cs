// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Pipeline;

internal sealed class NativeImportBinderFactory : INativeImportBinderFactory
{
    public INativeImportBinder Create(IEnumerable<string>? linkedLibraries) => new NativeLibraryBinding(linkedLibraries);
}
