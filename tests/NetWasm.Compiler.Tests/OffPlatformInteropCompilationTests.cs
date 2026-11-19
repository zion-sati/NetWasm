// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// The COM, Objective-C and Java interop types and the interop type maps bind and behave as upstream ships them where
// the platform has no such interop, as on Mono's WebAssembly targets: ComWrappers, ObjectiveCMarshal and JavaMarshal
// throw PlatformNotSupportedException (ComWrappers' lookups answer false), TypeMapping throws NotSupportedException, and
// ComVariant and StandardOleMarshalObject work, being plain managed types. Desktop .NET on Linux runs ComWrappers and the
// type maps, so these are checked against upstream's off-platform sources rather than desktop.
public sealed class OffPlatformInteropCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OffPlatformInteropTypesBindAndBehaveAsUpstreamShipsThem(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("OffPlatformInterop", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target));

        Assert.Equal(4095, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target,
            result.StaticDataEnd));
    }

    // The COM source generator's support types (System.Runtime.InteropServices.Marshalling) bind for a library built with
    // the generator. The plain types run here: the attributes, read as the generator's support reads
    // IUnknownDerivedAttribute<,> (an open generic filter), VirtualMethodTableInfo and the options.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ComSourceGeneratorSupportBindsAndBehavesAsUpstreamShipsIt(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("ComSourceGeneratorSupport", ComSource);
        // The support types live in System.Runtime.InteropServices, not the core library, and reach the core library's
        // platform imports, so the program targets the platform world.
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly,
            [.. TestAssets.WithFrameworkReferences(assembly, [assets.CoreLib])], "EntryPoint", "Run",
            [new("run", "EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Equal(63, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target,
            result.StaticDataEnd));
    }

    // The interface marshallers, ComVariantMarshaller and StrategyBasedComWrappers call COM through function pointers
    // (IUnknown's vtable), so a program that uses them links the unmanaged call trampolines those calls need, which
    // only the native runtime provides: the module validates and imports them. Their ComWrappers operations throw
    // PlatformNotSupportedException where they run, as the core library's ComWrappers does.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ComMarshallersLinkTheirUnmanagedCalls(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("StrategyBasedWrappers", """
            using System;
            using System.Runtime.InteropServices;
            using System.Runtime.InteropServices.Marshalling;

            [GeneratedComInterface, Guid("2c3f9903-b586-46b1-881b-adfce9af47b1")]
            public partial interface IShape { int Area(); }

            public sealed class Shape : IShape { public int Area() => 1; }

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    var score = 0;
                    try
                    {
                        new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(new IntPtr(1), CreateObjectFlags.None);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        score += 1;
                    }
                    score += ComInterfaceMarshaller<IShape>.ConvertToUnmanaged(input > 0 ? new Shape() : null) == null ? 2 : 0;
                    score += ComInterfaceMarshaller<IShape>.ConvertToManaged(null) is null ? 4 : 0;
                    score += UniqueComInterfaceMarshaller<IShape>.ConvertToManaged(null) is null ? 8 : 0;
                    var variant = ComVariantMarshaller.ConvertToUnmanaged(input + 42);
                    score += ComVariantMarshaller.ConvertToManaged(variant) is int value && value == input + 42 ? 16 : 0;
                    ComVariantMarshaller.Free(variant);
                    return score;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly,
            [.. TestAssets.WithFrameworkReferences(assembly, [assets.CoreLib])], "EntryPoint", "Run",
            [new("run", "EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        var trampolines = WasmModuleInspection.ReadFunctionImports(result.ApplicationModule)
            .Where(import => import.Name.StartsWith("netwasm_calli_", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(trampolines);
        Assert.All(trampolines, import => Assert.Equal("netwasm.runtime.v1", import.Module));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
    }

    private const string ComSource = """
        using System;
        using System.Reflection;
        using System.Runtime.InteropServices;
        using System.Runtime.InteropServices.Marshalling;

        [GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper), Guid("2c3f9903-b586-46b1-881b-adfce9af47b1")]
        public partial interface IShape { int Area(); }

        [ComExposedClass<ShapeDetails>]
        public sealed class Shape : IShape { public int Area() => 1; }

        public sealed unsafe class ShapeDetails : IComExposedClass
        {
            public static ComWrappers.ComInterfaceEntry* GetComInterfaceEntries(out int count)
            {
                count = 0;
                return null;
            }
        }

        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                var score = 0;
                var table = new VirtualMethodTableInfo((void*)1, (void**)2);
                table.Deconstruct(out var thisPointer, out var methods);
                score += thisPointer == (void*)1 && methods == (void**)2 ? 1 : 0;
                score += typeof(IShape).IsDefined(typeof(GeneratedComInterfaceAttribute), false) ? 2 : 0;
                score += ComInterfaceOptions.ManagedObjectWrapper != ComInterfaceOptions.None ? 4 : 0;
                // The support's own lookups: a type's exposed-class details through the open generic attribute, and
                // an interface without IUnknownDerivedAttribute<,>.
                score += typeof(Shape).GetCustomAttribute(typeof(ComExposedClassAttribute<>)) is IComExposedDetails ? 8 : 0;
                score += typeof(IShape).GetCustomAttribute(typeof(IUnknownDerivedAttribute<,>)) is null ? 16 : 0;
                score += typeof(Shape).IsDefined(typeof(ComExposedClassAttribute<>), true) ? 32 : 0;
                return score;
            }
        }
        """;

    private const string Source = """
        using System;
        using System.Collections;
        using System.Runtime.InteropServices;
        using System.Runtime.InteropServices.Java;
        using System.Runtime.InteropServices.Marshalling;
        using System.Runtime.InteropServices.ObjectiveC;

        public sealed class Wrappers : ComWrappers
        {
            protected override unsafe ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
            {
                count = 0;
                return null;
            }

            protected override object? CreateObject(IntPtr externalComObject, CreateObjectFlags flags) => null;

            protected override void ReleaseObjects(IEnumerable objects) { }
        }

        public sealed class Ole : StandardOleMarshalObject;

        public sealed class Group;

        public static class EntryPoint
        {
            private static int Throws<TException>(Action action, int bit) where TException : Exception
            {
                try
                {
                    action();
                    return 0;
                }
                catch (Exception exception)
                {
                    return exception.GetType() == typeof(TException) ? bit : 0;
                }
            }

            public static unsafe int Run(int input)
            {
                var score = 0;
                var wrappers = new Wrappers();
                score += ComWrappers.TryGetComInstance(new object(), out var unknown) || unknown != IntPtr.Zero ? 0 : 1;
                score += ComWrappers.TryGetObject(new IntPtr(1), out var wrapped) || wrapped is not null ? 0 : 2;
                score += Throws<PlatformNotSupportedException>(
                    () => wrappers.GetOrCreateComInterfaceForObject(new object(), CreateComInterfaceFlags.None), 4);
                score += Throws<PlatformNotSupportedException>(
                    () => wrappers.GetOrCreateObjectForComInstance(new IntPtr(1), CreateObjectFlags.UniqueInstance), 8);
                score += Throws<PlatformNotSupportedException>(() => ComWrappers.RegisterForTrackerSupport(wrappers), 16);
                score += Throws<PlatformNotSupportedException>(
                    () => ObjectiveCMarshal.CreateReferenceTrackingHandle(new object(), out _), 32);
                score += Throws<PlatformNotSupportedException>(
                    () => JavaMarshal.CreateReferenceTrackingHandle(new object(), null), 64);
                score += Throws<NotSupportedException>(() => TypeMapping.GetOrCreateExternalTypeMapping<Group>(), 128);
                score += Throws<NotSupportedException>(() => TypeMapping.GetOrCreateProxyTypeMapping<Group>(), 256);
                var variant = ComVariant.Create(42 + input);
                score += variant.VarType == VarEnum.VT_I4 && variant.As<int>() == 42 + input ? 512 : 0;
                score += ComVariant.Null.VarType == VarEnum.VT_NULL ? 1024 : 0;
                score += new Ole() is MarshalByRefObject ? 2048 : 0;
                return score;
            }
        }
        """;
}
