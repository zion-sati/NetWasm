// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class DelegateSignatureComparerTests
{
    [Fact]
    public void DelegateSignaturesRequireMatchingResultsAndParameters()
    {
        var scalar = MethodSignatureModel.Create(CliValueKind.I4);
        var noResult = MethodSignatureModel.Create(CliValueKind.Void);
        var withArgument = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.I4);

        Assert.True(DelegateSignatureComparer.AreEqual(scalar, scalar));
        Assert.False(DelegateSignatureComparer.AreEqual(scalar, noResult));
        Assert.False(DelegateSignatureComparer.AreEqual(scalar, withArgument));
    }
}
