// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

// A value type is described by the read assembly its identity names; a type that names no assembly, or one the
// compilation has not read, is not described here.
public sealed class LoadedAssemblyNativeStructSourceTests
{
    [Fact]
    public void AValueTypeIsDescribedByTheAssemblyItNames()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileSource("Structs", """
            public struct Pair { public int X; public bool Flag; }
            """));
        var source = new LoadedAssemblyNativeStructSourceFactory().Create(
            ImmutableDictionary<string, ManagedAssembly>.Empty.Add(assembly.Identity.Name, assembly));

        var pair = source.Describe(CliTypeIdentity.Named(assembly.Identity, "", "Pair", isValueType: true));

        Assert.Equal(["X", "Flag"], pair!.Value.Fields.Select(field => field.Name));
        Assert.Null(source.Describe(CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
        Assert.Null(source.Describe(CliTypeIdentity.Named(new AssemblyIdentity("Unread"), "", "Pair", isValueType: true)));
    }
}
