// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

// Vector64<T> and Vector2 keep 8 bytes of their v128, Vector3 12, and the others all 16.
public sealed class SimdVectorStorageTests
{
    private static readonly AssemblyIdentity Assembly = new("NetWasm.CoreLib");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    [Theory]
    [InlineData("System.Numerics", "Vector2", false, 8)]
    [InlineData("System.Numerics", "Vector3", false, 12)]
    [InlineData("System.Numerics", "Vector4", false, 16)]
    [InlineData("System.Runtime.Intrinsics", "Vector64`1", true, 8)]
    [InlineData("System.Runtime.Intrinsics", "Vector128`1", true, 16)]
    public void EachVectorTypeStoresItsWidth(string @namespace, string name, bool generic, int width)
    {
        var type = CliTypeIdentity.Named(Assembly, @namespace, name, true);

        Assert.Equal(width, SimdVectorStorage.Width(generic ? CliTypeIdentity.GenericInstantiation(type, [Int32]) : type));
    }

    [Fact]
    public void RejectsAMissingType() => Assert.Throws<ArgumentNullException>(() => SimdVectorStorage.Width(null!));
}
