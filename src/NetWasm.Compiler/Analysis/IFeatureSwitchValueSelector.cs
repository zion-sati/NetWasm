// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>Selects the value a method returns under the project's folded feature switches, or null.</summary>
internal interface IFeatureSwitchValueSelector
{
    bool? Select(MethodDefinitionModel method);
}
