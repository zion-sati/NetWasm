using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AssemblyNameContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AssemblyNamesParseFormatCloneAndRejectLoading(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AssemblyNameFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AssemblyNameFixture.EntryPoint",
            "Run", [new("run", "AssemblyNameFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        using System.Security;
        using System.IO;
        using System.Globalization;
        namespace AssemblyNameFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 30;
                var name = new AssemblyName("Example"); var rejected = 0;
                try { AssemblyName.GetAssemblyName("missing.dll"); } catch (PlatformNotSupportedException) { rejected++; }
                try { Assembly.Load(name); } catch (PlatformNotSupportedException) { rejected++; }
                try { AppDomain.CurrentDomain.Load(name); } catch (PlatformNotSupportedException) { rejected++; }
                try { AppDomain.CurrentDomain.ExecuteAssemblyByName(name, "arg"); } catch (PlatformNotSupportedException) { rejected++; }
                try { _ = name.KeyPair; } catch (PlatformNotSupportedException) { rejected++; }
                try { name.KeyPair = null; } catch (PlatformNotSupportedException) { rejected++; }
                try { name.GetObjectData(null!, default); } catch (PlatformNotSupportedException) { rejected++; }
                try { name.OnDeserialization(null); } catch (PlatformNotSupportedException) { rejected++; }
                if (rejected != 8 || name.Name != "Example") return 31;
                return input + 1;
            }
            public static int Portable(int input)
            {
                var name = new AssemblyName("Example, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null");
                if (name.Name != "Example" || name.Version != new Version(1,2,3,4) || name.CultureName != "" || name.GetPublicKeyToken()!.Length != 0) return 1;
                if (name.FullName != "Example, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null" || name.ToString() != name.FullName) return 2;
                var quoted = new AssemblyName("\"A, B\", Version=2.3");
                if (quoted.Name != "A, B" || new AssemblyName(quoted.FullName).Name != quoted.Name) return 3;
                try { new AssemblyName(""); return 4; } catch (ArgumentException) { }
                try { new AssemblyName(null!); return 5; } catch (ArgumentNullException) { }
                try { new AssemblyName("A, Version=bad"); return 6; } catch (FileLoadException) { }
                try { new AssemblyName("A, Version=1.2, Version=2.3"); return 7; } catch (FileLoadException) { }
                name.CodeBase = "file:///a b/é";
                if (name.EscapedCodeBase != "file:///a%20b/%C3%A9") return 8;
                name.ProcessorArchitecture = ProcessorArchitecture.Amd64;
                name.ContentType = AssemblyContentType.WindowsRuntime;
                name.Flags = AssemblyNameFlags.Retargetable;
                if (name.ProcessorArchitecture != ProcessorArchitecture.Amd64 || name.ContentType != AssemblyContentType.WindowsRuntime || name.Flags != AssemblyNameFlags.Retargetable) return 9;
                name.SetPublicKeyToken(new byte[] { 1,2,3,4,5,6,7,8 });
                var clone = (AssemblyName)name.Clone();
                if (ReferenceEquals(clone, name) || clone.FullName != name.FullName || ReferenceEquals(clone.GetPublicKeyToken(), name.GetPublicKeyToken())) return 10;
                clone.GetPublicKeyToken()![0] = 9;
                if (name.GetPublicKeyToken()![0] != 1) return 11;
                if (!AssemblyName.ReferenceMatchesDefinition(new AssemblyName("example, Version=9.0"), name)) return 12;
                if (!AssemblyName.ReferenceMatchesDefinition(null, null)) return 13;
                try { AssemblyName.ReferenceMatchesDefinition(null, name); return 14; } catch (ArgumentNullException) { }
                var ecma = new byte[16]; ecma[8] = 4;
                var key = new AssemblyName("Key"); key.SetPublicKey(ecma);
                if ((key.Flags & AssemblyNameFlags.PublicKey) == 0) return 15;
                var token = key.GetPublicKeyToken()!;
                var expected = new byte[] { 0xb7,0x7a,0x5c,0x56,0x19,0x34,0xe0,0x89 };
                for (var i=0;i<8;i++) if (token[i] != expected[i]) return 16;
                var keyClone = (AssemblyName)key.Clone();
                if (ReferenceEquals(keyClone.GetPublicKey(), key.GetPublicKey())) return 17;
                var bad = new AssemblyName(); bad.SetPublicKey(new byte[] { 1 });
                try { bad.GetPublicKeyToken(); return 18; } catch (SecurityException) { }
                var fresh = new AssemblyName();
                if (fresh.Name != null || fresh.FullName != "" || fresh.CultureName != null || fresh.GetPublicKeyToken() != null) return 19;
                fresh.CultureInfo = CultureInfo.InvariantCulture;
                fresh.CultureName = null;
                if (fresh.CultureInfo != null) return 20;
                fresh.HashAlgorithm = System.Configuration.Assemblies.AssemblyHashAlgorithm.SHA256;
                fresh.VersionCompatibility = System.Configuration.Assemblies.AssemblyVersionCompatibility.SameProcess;
                if (fresh.HashAlgorithm != System.Configuration.Assemblies.AssemblyHashAlgorithm.SHA256 || fresh.VersionCompatibility != System.Configuration.Assemblies.AssemblyVersionCompatibility.SameProcess) return 21;
                fresh.SetPublicKey(ecma);
                if (!ReferenceEquals(fresh.GetPublicKey(), ecma)) return 22;
                fresh.SetPublicKey(null);
                if (fresh.GetPublicKey() != null || (fresh.Flags & AssemblyNameFlags.PublicKey) != 0) return 23;
                fresh.SetPublicKeyToken(Array.Empty<byte>());
                if (fresh.GetPublicKeyToken()!.Length != 0) return 24;
                fresh.CodeBase = null;
                if (fresh.CodeBase != null || fresh.EscapedCodeBase != null) return 25;
                return input + 1;
            }
        }
        """;
}
