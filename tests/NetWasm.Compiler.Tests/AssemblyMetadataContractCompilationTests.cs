using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AssemblyMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MetadataForwardingWorksAndRuntimeDiscoveryRejects(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AssemblyMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AssemblyMetadataFixture.EntryPoint",
            "Run", [new("run", "AssemblyMetadataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.IO;
        using System.Reflection;
        using System.Globalization;
        using System.Configuration.Assemblies;
        namespace AssemblyMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                Assembly a = new EmptyAssembly();
                Action[] calls = {
                    () => { _ = a.CodeBase; }, () => { _ = a.EntryPoint; },
                    () => { _ = a.ImageRuntimeVersion; }, () => { _ = a.Location; }, () => { _ = a.ReflectionOnly; },
                    () => { _ = a.GlobalAssemblyCache; }, () => { _ = a.HostContext; }, () => { _ = a.EscapedCodeBase; },
                    () => a.GetReferencedAssemblies(),
                    () => a.GetType("X"), () => a.GetType("X", true), () => a.GetType("X", true, true),
                    () => a.GetTypes(), () => a.GetExportedTypes(), () => a.GetForwardedTypes(), () => { _ = a.ExportedTypes; },
                    () => a.GetManifestResourceNames(), () => a.GetManifestResourceStream("X"), () => a.GetManifestResourceStream(typeof(int), "X"),
                    () => a.GetFile("X"), () => a.GetFiles(), () => a.GetFiles(true),
                    () => a.GetSatelliteAssembly(CultureInfo.InvariantCulture), () => a.GetSatelliteAssembly(CultureInfo.InvariantCulture, new Version(1,0)),
                    () => a.CreateInstance("X"), () => a.CreateInstance("X", true), () => a.CreateInstance("X", true, BindingFlags.Public, null, null, null, null),
                    () => Assembly.GetCallingAssembly(),
                    () => Assembly.LoadFile("X"), () => Assembly.LoadFrom("X"), () => Assembly.LoadFrom("X", null, System.Configuration.Assemblies.AssemblyHashAlgorithm.None),
                    () => Assembly.LoadWithPartialName("X"), () => Assembly.ReflectionOnlyLoad(new byte[0]), () => Assembly.ReflectionOnlyLoad("X"),
                    () => Assembly.ReflectionOnlyLoadFrom("X"), () => Assembly.UnsafeLoadFrom("X")
                };
                var rejected = 0;
                foreach (var call in calls) { try { call(); } catch (PlatformNotSupportedException e) { if (e.Message.Length == 0) return 21; rejected++; } }
                if (rejected != calls.Length) return 22;
                // An assembly answers its identity from the runtime type table, named only from the type-symbols
                // sidecar, and the program's own assembly is the executing and entry one.
                if (a.FullName != "<Assembly name unavailable>" || a.ToString() != a.FullName || a.GetName().Name != a.FullName) return 23;
                if (Assembly.GetExecutingAssembly() != typeof(EntryPoint).Assembly ||
                    Assembly.GetEntryAssembly() != typeof(EntryPoint).Assembly) return 24;
                // GetAssembly is the type's assembly, as upstream returns it.
                if (Assembly.GetAssembly(typeof(int)) != typeof(int).Assembly ||
                    Assembly.GetAssembly(typeof(EntryPoint)) != typeof(EntryPoint).Assembly) return 25;
                return input + 1;
            }
            public static int Portable(int input)
            {
                var custom = new CustomAssembly(); Assembly a = custom;
                if (a.IsDynamic || !a.IsFullyTrusted || a.SecurityRuleSet != System.Security.SecurityRuleSet.None) return 1;
                if (a.ToString() != "Custom" || a.EscapedCodeBase != "file:///a%20b") return 2;
                if (a.GetName().Name != "Custom" || custom.Copied) return 3;
                a.GetName(true); if (!custom.Copied) return 4;
                if (a.GetType("X") != typeof(int) || custom.Throw || custom.Ignore) return 5;
                a.GetType("X", true); if (!custom.Throw || custom.Ignore) return 6;
                a.GetType("X", true, true); if (!custom.Ignore) return 7;
                if (a.GetFiles().Length != 0 || custom.Resources) return 8;
                a.GetFiles(true); if (!custom.Resources) return 9;
                var count = 0; foreach (var type in a.ExportedTypes) { if (type != typeof(int)) return 10; count++; }
                if (count != 1) return 11;
                if (!ReferenceEquals(a.CreateInstance("X"), custom.Marker) || custom.Ignore || custom.Flags != (BindingFlags.Public | BindingFlags.Instance)) return 12;
                a.CreateInstance("X", true); if (!custom.Ignore) return 13;
                if (Assembly.CreateQualifiedName("Asm", "Type") != "Type, Asm" || Assembly.CreateQualifiedName(null, null) != ", ") return 14;
                try { Assembly.GetAssembly(null!); return 15; } catch (ArgumentNullException) { }
                return input + 1;
            }
            private sealed class EmptyAssembly : Assembly { }
            private sealed class CustomAssembly : Assembly
            {
                public bool Copied, Throw, Ignore, Resources;
                public BindingFlags Flags;
                public readonly object Marker = new object();
                public override string FullName => "Custom";
                public override string CodeBase => "file:///a b";
                public override AssemblyName GetName(bool copiedName) { Copied = copiedName; return new AssemblyName("Custom"); }
                public override Type GetType(string name, bool throwOnError, bool ignoreCase) { Throw = throwOnError; Ignore = ignoreCase; return typeof(int); }
                public override FileStream[] GetFiles(bool getResourceModules) { Resources = getResourceModules; return Array.Empty<FileStream>(); }
                public override Type[] GetExportedTypes() => new[] { typeof(int) };
                public override object CreateInstance(string name, bool ignore, BindingFlags flags, Binder? binder, object[]? args, CultureInfo? culture, object[]? activation) { Ignore = ignore; Flags = flags; return Marker; }
            }
        }
        """;
}
