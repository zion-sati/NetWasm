// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeVariadicCallPlanner
{
    // How many parameters a native function declared with these different native signatures takes before its variadic
    // ones, or null when NetWasm cannot call it as a variadic function.
    int? FixedParameters(string libraryName, string entryPoint, IReadOnlyCollection<MethodSignatureModel> declarations);
}
