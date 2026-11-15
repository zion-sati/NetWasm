// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// Each typed-reference instruction calls one TypedReference helper; TypedReferenceCompilationTests runs them.
public sealed class TypedReferenceCallResolverTests
{
    [Fact]
    public void AnInstructionOutsideTheThreeHasNoHelper() =>
        Assert.Throws<ArgumentOutOfRangeException>("instruction", () =>
            new TypedReferenceCallResolver(null!, null!, null!, null!).Resolve((TypedReferenceInstruction)3, []));
}
