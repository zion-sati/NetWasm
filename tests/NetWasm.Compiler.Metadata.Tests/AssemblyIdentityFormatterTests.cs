// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class AssemblyIdentityFormatterTests
{
    [Fact]
    public void FormatCalculatesThePublicKeyTokenForAFrameworkAssembly()
    {
        using var assembly = ManagedAssemblyTestFactory.Load(typeof(object).Assembly.Location);
        var formatter = new AssemblyIdentityFormatterFactory().Create([assembly]);

        var formatted = formatter.Format(assembly.Identity);

        Assert.Contains("PublicKeyToken=", formatted);
        Assert.DoesNotContain("PublicKeyToken=null", formatted);
    }

    [Fact]
    public void FormatRetainsAnExplicitAssemblyCulture()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource(
            "CulturedAssembly",
            """
            using System;

            [assembly: System.Reflection.AssemblyCulture("en-US")]

            namespace System.Reflection
            {
                public sealed class AssemblyCultureAttribute(string culture) : Attribute
                {
                    public string Culture { get; } = culture;
                }
            }

            public static class EntryPoint { }
            """);
        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var formatter = new AssemblyIdentityFormatterFactory().Create([assembly]);

        var formatted = formatter.Format(assembly.Identity);

        Assert.Contains("Culture=en-US", formatted);
    }
}
