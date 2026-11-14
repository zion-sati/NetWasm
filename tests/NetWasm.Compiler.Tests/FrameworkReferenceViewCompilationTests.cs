using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FrameworkReferenceViewCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void OriginalTokensExecutePlatformMembersWithoutChangingTheDll(WasmTarget target, bool externalCaller)
    {
        using var assets = TestAssets.Create();
        var platform = assets.CompileSource("Forwarding.Platform", """
            namespace Shared;
            public static class Counter
            {
                public static int Value;
                public static int Read() => 40;
            }
            """);
        var original = assets.CompileSource("Forwarding.Backport", """
            namespace Shared;
            public static class Counter
            {
                public static int Value;
                public static int Read() => 900;
            }
            public static class PackageOnly
            {
                public static int Run(int input)
                {
                    Counter.Value = input;
                    return Counter.Read() + Counter.Value + 1;
                }
            }
            """);
        var originalBytes = File.ReadAllBytes(original);
        var view = Path.Combine(assets.Directory, "Backport.reference.dll");
        File.WriteAllBytes(view, Forwarder());

        var entry = externalCaller ? assets.CompileSource("Forwarding.Caller", """
            namespace Caller;
            public static class Entry
            {
                public static int Run(int input)
                {
                    Shared.Counter.Value = input;
                    return Shared.Counter.Read() + Shared.Counter.Value + 1;
                }
            }
            """, original) : original;
        var options = new CompilerOptions(entry,
            externalCaller ? [original, platform, assets.CoreLib] : [platform, assets.CoreLib],
            externalCaller ? "Caller.Entry" : "Shared.PackageOnly", "Run", [])
        {
            Target = target,
            MetadataReferencePaths = [view]
        };
        var result = NetWasmCompiler.Compile(options);

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 1, target, result.StaticDataEnd));
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void UnsupportedConstraintIsRejectedOnlyWhenReached(WasmTarget target, bool reachMismatch)
    {
        using var assets = TestAssets.Create();
        var platform = assets.CompileSource("Forwarding.Platform", """
            namespace Shared;
            public static class Counter
            {
                public static int Read<T>() where T : class => 40;
            }
            """);
        var original = assets.CompileSource("Forwarding.Backport", """
            namespace Shared;
            public static class Counter
            {
                public static int Read<T>() => 900;
            }
            public static class PackageOnly
            {
                public static int Supported(int input) => 42;
                public static int Unsupported(int input) => Counter.Read<object>() + 2;
            }
            """);
        var view = Path.Combine(assets.Directory, "Backport.reference.dll");
        File.WriteAllBytes(view, Forwarder());
        var options = new CompilerOptions(original, [platform, assets.CoreLib],
            "Shared.PackageOnly", reachMismatch ? "Unsupported" : "Supported", [])
        {
            Target = target,
            MetadataReferencePaths = [view]
        };
        if (reachMismatch)
        {
            var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
            Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
            Assert.Contains("generic parameter requirement", error.Message);
            Assert.Contains("Forwarding.Backport::Read", error.Message);
            return;
        }
        var result = NetWasmCompiler.Compile(options);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ChangedFieldMutabilityIsRejectedOnlyWhenReached(WasmTarget target, bool reachMismatch)
    {
        using var assets = TestAssets.Create();
        var platform = assets.CompileSource("Forwarding.Platform", """
            namespace Shared;
            public static class Counter { public static readonly int Value = 40; }
            """);
        var original = assets.CompileSource("Forwarding.Backport", """
            namespace Shared;
            public static class Counter { public static int Value; }
            """);
        var caller = assets.CompileSource("Forwarding.Caller", """
            namespace Caller;
            public static class Entry
            {
                public static int Supported(int input) => 42;
                public static int Unsupported(int input)
                {
                    Shared.Counter.Value = input;
                    return Shared.Counter.Value;
                }
            }
            """, original);
        var view = Path.Combine(assets.Directory, "Backport.reference.dll");
        File.WriteAllBytes(view, Forwarder());
        var options = new CompilerOptions(caller, [original, platform, assets.CoreLib],
            "Caller.Entry", reachMismatch ? "Unsupported" : "Supported", [])
        {
            Target = target,
            MetadataReferencePaths = [view]
        };
        if (reachMismatch)
        {
            var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
            Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
            Assert.Contains("mutability", error.Message);
            Assert.Contains("Forwarding.Backport::Value", error.Message);
            return;
        }
        var result = NetWasmCompiler.Compile(options);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnmappedUnusedSameNamedTypeDoesNotRejectThePackage(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var platform = assets.CompileSource("Forwarding.Platform", """
            namespace Shared;
            public static class Counter { public static int Read() => 42; }
            public interface Retained { }
            """);
        var original = assets.CompileSource("Forwarding.Backport", """
            namespace Shared;
            public static class Counter { public static int Read() => 900; }
            public class Retained { }
            public static class PackageOnly
            {
                public static int Run(int input) => Counter.Read();
            }
            """);
        var view = Path.Combine(assets.Directory, "Backport.reference.dll");
        File.WriteAllBytes(view, Forwarder());
        var options = new CompilerOptions(original, [platform, assets.CoreLib], "Shared.PackageOnly", "Run", [])
        {
            Target = target,
            MetadataReferencePaths = [view]
        };
        var result = NetWasmCompiler.Compile(options);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void RuntimeFacadeAndReferenceViewMayForwardThroughDifferentScopes(WasmTarget target, bool legacyAlias)
    {
        using var assets = TestAssets.Create();
        var platform = assets.CompileSource("Forwarding.Platform", """
            namespace Shared;
            public static class Counter { public static int Read() => 42; }
            """);
        var entry = assets.CompileSource("Forwarding.Caller", """
            namespace Caller;
            public static class Entry { public static int Run(int input) => 42; }
            """);
        var runtimeFacade = Path.Combine(assets.Directory, "runtime-facade.dll");
        var intermediateFacade = Path.Combine(assets.Directory, "intermediate-facade.dll");
        var view = Path.Combine(assets.Directory, "reference-view.dll");
        File.WriteAllBytes(runtimeFacade, Forwarder("Forwarding.Backport", legacyAlias ? "Legacy.Platform" : "Forwarding.Middle"));
        File.WriteAllBytes(intermediateFacade, Forwarder("Forwarding.Middle", "Forwarding.Platform"));
        File.WriteAllBytes(view, Forwarder());
        var options = new CompilerOptions(entry,
            [runtimeFacade, intermediateFacade, platform, assets.CoreLib], "Caller.Entry", "Run", [])
        {
            Target = target,
            MetadataReferencePaths = [view],
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty
        };
        var result = NetWasmCompiler.Compile(options);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    private static byte[] Forwarder(string source = "Forwarding.Backport", string destination = "Forwarding.Platform")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Backport.reference.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(source), new Version(1, 0),
            default, default, 0, 0);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var owner = metadata.AddAssemblyReference(metadata.GetOrAddString(destination),
            new Version(1, 0), default, default, 0, default);
        metadata.AddExportedType(TypeAttributes.Public | (TypeAttributes)0x00200000,
            metadata.GetOrAddString("Shared"), metadata.GetOrAddString("Counter"), owner, 0);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder())
            .Serialize(image);
        return image.ToArray();
    }
}
