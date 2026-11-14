using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MemberDescriptorBoundaryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DiscoveryBoundariesThrowCatchableExceptions(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DescriptorFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "DescriptorFixture.EntryPoint",
            "Run", [new("run", "DescriptorFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        using System.Reflection;
        namespace DescriptorFixture;
        public sealed class Descriptor : MemberDescriptor
        {
            public Descriptor() : base("item") { }
            public object Target(Type type, object instance) => GetInvocationTarget(type, instance);
            public static object Invokee(Type type, object instance) => GetInvokee(type, instance);
            public static MethodInfo Find(Type type, bool publicOnly) => FindMethod(type, "Run", new Type[] { typeof(int) }, typeof(int), publicOnly);
            public static MethodInfo FindPublic(Type type) => FindMethod(type, "Run", new Type[] { typeof(int) }, typeof(int));
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var descriptor = new Descriptor();
                var failures = 0;
                try { descriptor.Target(typeof(Descriptor), descriptor); } catch (PlatformNotSupportedException e) { if (!string.IsNullOrEmpty(e.Message)) failures++; }
                try { Descriptor.Invokee(typeof(Descriptor), descriptor); } catch (PlatformNotSupportedException e) { if (!string.IsNullOrEmpty(e.Message)) failures++; }
                try { Descriptor.Find(typeof(EntryPoint), true); } catch (PlatformNotSupportedException) { failures++; }
                try { Descriptor.Find(typeof(EntryPoint), false); } catch (PlatformNotSupportedException) { failures++; }
                try { Descriptor.FindPublic(typeof(EntryPoint)); } catch (PlatformNotSupportedException) { failures++; }
                try { var value = descriptor.Category; } catch (PlatformNotSupportedException) { failures++; }
                try { var value = descriptor.Description; } catch (PlatformNotSupportedException) { failures++; }
                try { var value = descriptor.DisplayName; } catch (PlatformNotSupportedException) { failures++; }
                try { var value = descriptor.IsBrowsable; } catch (PlatformNotSupportedException) { failures++; }
                try { var value = descriptor.DesignTimeOnly; } catch (PlatformNotSupportedException) { failures++; }
                try { Descriptor.Find(null, true); } catch (ArgumentNullException e) { if (e.ParamName == "componentClass") failures++; }
                try { Descriptor.Invokee(null, descriptor); } catch (ArgumentNullException e) { if (e.ParamName == "componentClass") failures++; }
                try { Descriptor.Invokee(typeof(Descriptor), null); } catch (ArgumentNullException e) { if (e.ParamName == "component") failures++; }
                return failures == 13 ? input + 1 : failures;
            }
        }
        """;
}
