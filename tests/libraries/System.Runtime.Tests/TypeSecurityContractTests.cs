// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's TypeSecurityContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class TypeSecurityContractTests
{
    [Test]
    public async Task SecurityTransparencyFactsMatchDesktop() => await Assert.That(TypeSecurityFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
