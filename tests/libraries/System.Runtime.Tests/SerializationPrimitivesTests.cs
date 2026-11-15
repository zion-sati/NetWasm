// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's SerializationPrimitivesCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class SerializationPrimitivesTests
{
    [Test]
    public async Task DataContractAttributesAndFormatterBasesMatchDesktop() => await Assert.That(SerializationPrimitivesFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
