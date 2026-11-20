// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// AssemblyLoadContext over a NetWasm program: the default context holds the program's assemblies and resolves names as
// Assembly.Load does, a context the program creates resolves through its own Load first, unloading and contextual
// reflection behave as .NET's, and loading from a path, a stream or a native library throws, as Assembly.LoadFrom
// does. Microsoft.Testing.Platform reaches it to look for optional extensions.
public sealed class AssemblyLoadContextCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TheDefaultContextHoldsTheProgramAndLoadsNothingAtRunTime(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"LoadContexts{target}", """
            using System;
            using System.IO;
            using System.Linq;
            using System.Reflection;
            using System.Runtime.Loader;

            public sealed class Aliasing : AssemblyLoadContext
            {
                public Aliasing() : base("aliasing", isCollectible: true) { }

                protected override Assembly? Load(AssemblyName assemblyName) =>
                    assemblyName.Name == "Alias" ? typeof(EntryPoint).Assembly : null;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var own = typeof(EntryPoint).Assembly;
                    var context = AssemblyLoadContext.Default;
                    if (context.Name != "Default" || context.IsCollectible) return 1;
                    if (!context.Assemblies.Contains(own) || AssemblyLoadContext.GetLoadContext(own) != context) return 2;
                    if (context.LoadFromAssemblyName(own.GetName()) != own) return 3;
                    var aliasing = new Aliasing();
                    if (aliasing.LoadFromAssemblyName(new AssemblyName("Alias")) != own) return 4;
                    if (aliasing.LoadFromAssemblyName(own.GetName()) != own || aliasing.Assemblies.Any()) return 5;
                    if (!AssemblyLoadContext.All.Contains(aliasing)) return 6;
                    try { context.LoadFromAssemblyPath("/plugins/Extension.dll"); return 7; }
                    catch (PlatformNotSupportedException) { }
                    try { context.LoadFromStream(new MemoryStream()); return 8; }
                    catch (PlatformNotSupportedException) { }
                    try { context.Unload(); return 9; }
                    catch (InvalidOperationException) { }
                    var unloaded = 0;
                    aliasing.Unloading += _ => unloaded++;
                    aliasing.Unload();
                    aliasing.Unload();
                    if (unloaded != 1 || AssemblyLoadContext.All.Contains(aliasing)) return 10;
                    using (aliasing.EnterContextualReflection())
                    {
                        if (AssemblyLoadContext.CurrentContextualReflectionContext != aliasing) return 11;
                    }
                    return AssemblyLoadContext.CurrentContextualReflectionContext is null ? input + 1 : 12;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    // The rest of System.Runtime.Loader as Native AOT has it: no dependency manifest to resolve components from, no
    // metadata or IL updates and no raw metadata, while libraries' metadata update handler attributes stay valid.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompiledProgramsTakeNoComponentsOrMetadataUpdates(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource($"LoaderContract{target}", """
            using System;
            using System.Reflection;
            using System.Reflection.Metadata;
            using System.Runtime.Loader;

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    try { _ = new AssemblyDependencyResolver("/plugins/Extension.dll"); return 1; }
                    catch (PlatformNotSupportedException) { }
                    if (MetadataUpdater.IsSupported) return 2;
                    try { MetadataUpdater.ApplyUpdate(typeof(EntryPoint).Assembly, [], [], []); return 3; }
                    catch (PlatformNotSupportedException) { }
                    if (typeof(EntryPoint).Assembly.TryGetRawMetadata(out var blob, out var length) || blob != null || length != 0) return 4;
                    try { AssemblyExtensions.TryGetRawMetadata(null!, out _, out _); return 5; }
                    catch (ArgumentNullException) { }
                    return new MetadataUpdateHandlerAttribute(typeof(EntryPoint)).HandlerType == typeof(EntryPoint) ? input + 1 : 6;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
