using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AppDomainAssemblyContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AssemblyPayloadsWorkAndUnsupportedOperationsReject(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DomainAssemblyFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "DomainAssemblyFixture.EntryPoint",
            "Run", [new("run", "DomainAssemblyFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        using System.Runtime.Serialization;
        namespace DomainAssemblyFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                var domain = AppDomain.CurrentDomain; var rejected = 0; var calls = 0;
                var bytes = new byte[] { 1, 2 }; var symbols = new byte[] { 3 };
                try { domain.Load(bytes); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.Load(bytes, symbols); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.Load("Missing"); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.GetAssemblies(); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ReflectionOnlyGetAssemblies(); } catch (PlatformNotSupportedException) { rejected++; }
                AssemblyLoadEventHandler loaded = (sender,e) => calls++;
                ResolveEventHandler resolve = (sender,e) => { calls++; return null; };
                try { domain.AssemblyLoad += loaded; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.AssemblyLoad -= loaded; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.AssemblyResolve += resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.AssemblyResolve -= resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ReflectionOnlyAssemblyResolve += resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ReflectionOnlyAssemblyResolve -= resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ResourceResolve += resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ResourceResolve -= resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.TypeResolve += resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.TypeResolve -= resolve; } catch (PlatformNotSupportedException) { rejected++; }
                try { Assembly.Load(bytes); } catch (PlatformNotSupportedException) { rejected++; }
                try { Assembly.Load(bytes, symbols); } catch (PlatformNotSupportedException) { rejected++; }
                try { Assembly.Load("Missing"); } catch (PlatformNotSupportedException) { rejected++; }
                Assembly assembly = new FakeAssembly();
                try { assembly.GetCustomAttributes(false); } catch (PlatformNotSupportedException) { rejected++; }
                try { assembly.GetCustomAttributes(typeof(ObsoleteAttribute), false); } catch (PlatformNotSupportedException) { rejected++; }
                try { assembly.IsDefined(typeof(ObsoleteAttribute), false); } catch (PlatformNotSupportedException) { rejected++; }
                try { ((ISerializable)assembly).GetObjectData(null!, default); } catch (PlatformNotSupportedException) { rejected++; }
                if (rejected != 22 || calls != 0 || bytes[0] != 1 || bytes[1] != 2 || symbols[0] != 3) return 21;
                return input + 1;
            }
            public static int Portable(int input)
            {
                Assembly assembly = new FakeAssembly();
                Assembly? nothing = null;
                Assembly alias = assembly;
                if (assembly == null || null == assembly || nothing != null || !(assembly == alias)) return 6;
                var another = new FakeAssembly();
                if (assembly == another || assembly.Equals(another) || !assembly.Equals(alias)) return 7;
                if (assembly.GetHashCode() != alias.GetHashCode()) return 8;
                var equal = new EqualAssembly(); Assembly comparable = equal;
                if (!(comparable == assembly) || equal.Calls != 1) return 9;
                if (comparable == null || null == comparable || equal.Calls != 1) return 10;
                var loaded = new AssemblyLoadEventArgs(assembly);
                if (!ReferenceEquals(loaded.LoadedAssembly, assembly) || new AssemblyLoadEventArgs(null!).LoadedAssembly != null) return 1;
                var resolve = new ResolveEventArgs("Target", assembly);
                if (resolve.Name != "Target" || !ReferenceEquals(resolve.RequestingAssembly, assembly)) return 2;
                var empty = new ResolveEventArgs(null!);
                if (empty.Name != null || empty.RequestingAssembly != null) return 3;
                var calls = 0;
                AssemblyLoadEventHandler load = (sender,e) => { if (ReferenceEquals(e, loaded)) calls++; };
                load(null!, loaded);
                ResolveEventHandler first = (sender,e) => { calls++; return assembly; };
                ResolveEventHandler last = (sender,e) => { calls++; return null; };
                var both = first + last;
                if (both(null, resolve) != null || calls != 3) return 4;
                both -= last;
                if (!ReferenceEquals(both!(null, resolve), assembly) || calls != 4) return 5;
                return input + 1;
            }
            private sealed class FakeAssembly : Assembly { }
            private sealed class EqualAssembly : Assembly
            {
                public int Calls;
                public override bool Equals(object? other) { Calls++; return other is Assembly; }
                public override int GetHashCode() => 1;
            }
        }
        """;
}
