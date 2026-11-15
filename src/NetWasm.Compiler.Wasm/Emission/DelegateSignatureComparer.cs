// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class DelegateSignatureComparer
{
    public static bool AreEqual(
        MethodSignatureModel target,
        MethodSignatureModel invoke) =>
        target.ReturnSignatureType.Equals(invoke.ReturnSignatureType) &&
        target.ParameterSignatureTypes.SequenceEqual(invoke.ParameterSignatureTypes);
}
