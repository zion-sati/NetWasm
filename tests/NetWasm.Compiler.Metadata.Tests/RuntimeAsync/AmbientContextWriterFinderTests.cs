// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

// A program whose assemblies, the core library aside, refer to an API that changes an ambient context keeps one body per
// runtime-async method: setting an AsyncLocal<T>'s value (a generic instantiation), suppressing the execution context's
// flow, setting a synchronization context, or a core library member that sets an AsyncLocal of its own (cultures, the
// principal, contextual reflection, the current Activity). Reading any of them, or capturing the context, changes
// nothing.
public sealed class AmbientContextWriterFinderTests
{
    [Theory]
    [InlineData("new System.Threading.AsyncLocal<int>().Value = 1;", true)]
    [InlineData("System.Threading.ExecutionContext.SuppressFlow();", true)]
    [InlineData("System.Threading.SynchronizationContext.SetSynchronizationContext(null);", true)]
    [InlineData("System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;", true)]
    [InlineData("System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;", true)]
    [InlineData("System.Threading.Thread.CurrentPrincipal = null;", true)]
    [InlineData("using var scope = System.Runtime.Loader.AssemblyLoadContext.Default.EnterContextualReflection();", true)]
    [InlineData("System.Diagnostics.Activity.Current = null;", true)]
    [InlineData("new System.Diagnostics.Activity(\"work\").Start().Stop();", true)]
    [InlineData("_ = new System.Diagnostics.ActivitySource(\"source\").StartActivity(\"work\");", true)]
    [InlineData("_ = System.Globalization.CultureInfo.CurrentCulture; _ = System.Diagnostics.Activity.Current;", false)]
    [InlineData("_ = new System.Threading.AsyncLocal<int>().Value; _ = System.Threading.ExecutionContext.Capture();", false)]
    [InlineData("", false)]
    public void FindsAProgramThatChangesAContext(string statement, bool finds)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("ContextWriters", $$"""
            public static class Program
            {
                public static void Run()
                {
                    {{statement}}
                }
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(library, [assets.CoreLib]);

        Assert.Equal(finds, new AmbientContextWriterFinder().Finds(lease.Snapshot));
    }
}
