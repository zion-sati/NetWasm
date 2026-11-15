// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class TypedStackValidatorFactoryTests
{
    [Fact]
    public void FactoryRejectsMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TypedStackValidatorFactory(
                null!));
    }

    [Fact]
    public void FactoryCreatesAValidatorFromNarrowRepositories()
    {
        var program = new FakeProgram();

        Assert.NotNull(((ITypedStackValidatorFactory)new TypedStackValidatorFactory(
            new StackTypeCompatibilityValidator())).Create(
                program,
                program,
                program));
    }
}
