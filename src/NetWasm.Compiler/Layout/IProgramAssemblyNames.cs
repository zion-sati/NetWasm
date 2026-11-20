// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

// The display name of a program assembly, as the program assembly table describes it.
internal interface IProgramAssemblyNames
{
    string DisplayName(AssemblyIdentity assembly);
}
