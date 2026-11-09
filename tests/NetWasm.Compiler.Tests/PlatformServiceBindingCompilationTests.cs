using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A program declares its platform with NetWasmPlatformAttribute: .NET's system APIs reach the
// facades that platform provides, and the default platform it replaces is not reached.
public sealed class PlatformServiceBindingCompilationTests
{
    private const string Bound = """
        using System;
        using System.Runtime.InteropServices.WebAssembly;

        [assembly: NetWasmPlatform(typeof(BoundServices.HostPlatform))]

        namespace BoundServices;

        public sealed class FixedClock : PlatformClock
        {
            public override DateTime GetUtcNow() => new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        }

        // A facade implemented directly, without its base.
        public sealed class CountingSource : IRandomSource
        {
            public void Fill(Span<byte> destination)
            {
                for (var index = 0; index < destination.Length; index++)
                {
                    destination[index] = (byte)(index + 1);
                }
            }
        }

        public sealed class HostPlatform : NetWasmPlatform
        {
            public override IClock Clock => new FixedClock();

            public override IRandomSource Random => new CountingSource();
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var now = DateTime.UtcNow;
                Span<byte> bytes = stackalloc byte[4];
                System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                // FixedClock leaves the monotonic clock unprovided; the message names the platform.
                var named = 0;
                try
                {
                    System.Diagnostics.Stopwatch.GetTimestamp();
                }
                catch (PlatformNotSupportedException exception) when (
                    exception.Message == "The monotonic clock is not available: the program's platform, " +
                        "'BoundServices.HostPlatform', declared in BoundServices, does not provide it (NetWasmPlatform.Clock).")
                {
                    named = 1_000_000;
                }

                return named + now.Year * 100 + now.Hour + bytes[3] + input;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SystemApisReachTheDeclaredPlatformInsteadOfWasi(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("BoundServices", Bound);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "BoundServices.EntryPoint", "Run", [], Target: target));

        var reached = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BoundServices.HostPlatform", reached);
        Assert.Contains("BoundServices.FixedClock", reached);
        Assert.Contains("BoundServices.CountingSource", reached);
        Assert.DoesNotContain("System.Runtime.InteropServices.WebAssembly.WasiPlatform", reached);
        Assert.DoesNotContain("System.Runtime.InteropServices.WasiClock", reached);
        Assert.DoesNotContain(
            "System.Runtime.InteropServices.Cryptography.WasiCryptographicallySecureRandomByteFiller", reached);
        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport?.InterfaceName is "wasi:clocks/system-clock@0.3.2" or "wasi:random/random@0.3.2");
        Assert.Equal(1_000_000 + 2026 * 100 + 12 + 4 + 5,
            ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 5, target, result.StaticDataEnd));
    }

    // A host package can ship and declare the platform; the application can declare its own instead.
    [Fact]
    public void APlatformFromALibraryBindsAndTheApplicationsDeclarationWins()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("HostPackage", """
            using System;
            using System.Runtime.InteropServices.WebAssembly;

            namespace HostPackage;

            public class HostPlatform : NetWasmPlatform
            {
                public override IClock Clock => new HostClock();
            }

            public sealed class HostClock : PlatformClock
            {
                public override DateTime GetUtcNow() => new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            """);
        var application = assets.CompileSource("HostApplication", """
            using System;
            using System.Runtime.InteropServices.WebAssembly;

            [assembly: NetWasmPlatform(typeof(HostApplication.AppPlatform))]

            namespace HostApplication;

            public sealed class AppPlatform : HostPackage.HostPlatform
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input) => DateTime.UtcNow.Year + input;
            }
            """, library);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            application, [assets.CoreLib, library], "HostApplication.EntryPoint", "Run", []));
        Assert.Contains(result.Program.Methods.Values, body => body.Method.DeclaringType.FullName == "HostPackage.HostClock");

        // The application's declaration takes precedence over a package's.
        var declaring = assets.CompileSource("DeclaringPackage", PackageDeclaring("DeclaringPackage"));
        var overriding = assets.CompileSource("OverridingApplication", """
            using System;

            [assembly: System.Runtime.InteropServices.WebAssembly.NetWasmPlatform(typeof(OverridingApplication.Own))]

            namespace OverridingApplication;

            public sealed class Own : System.Runtime.InteropServices.WebAssembly.NetWasmPlatform
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input) => DateTime.UtcNow.Year + input;
            }
            """, declaring);
        var overridden = NetWasmCompiler.Compile(new CompilerOptions(
            overriding, [assets.CoreLib, declaring], "OverridingApplication.EntryPoint", "Run", []));
        var reached = overridden.Program.Methods.Values.Concat(overridden.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("OverridingApplication.Own", reached);
        Assert.DoesNotContain("DeclaringPackage.PackagePlatform", reached);

        // A package's declaration applies when the application makes none.
        var undeclared = assets.CompileSource("UndeclaredApplication", """
            namespace UndeclaredApplication;

            public static class EntryPoint
            {
                public static int Run(int input) => System.DateTime.UtcNow.Year + input;
            }
            """);
        var packaged = NetWasmCompiler.Compile(new CompilerOptions(
            undeclared, [assets.CoreLib, declaring], "UndeclaredApplication.EntryPoint", "Run", []));
        Assert.Contains(packaged.Program.Methods.Values.Concat(packaged.Program.ConstructedMethods.Values),
            body => body.Method.DeclaringType.FullName == "DeclaringPackage.PackagePlatform");

        // Two packages' declarations, and none in the application, are ambiguous.
        var other = assets.CompileSource("OtherPackage", PackageDeclaring("OtherPackage"));
        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            undeclared, [assets.CoreLib, declaring, other], "UndeclaredApplication.EntryPoint", "Run", [])));
        Assert.Contains("'DeclaringPackage' and 'OtherPackage' declare one each", exception.Message);
        Assert.Contains("declare the platform in the application to choose", exception.Message);
    }

    private static string PackageDeclaring(string name) => $$"""
        [assembly: System.Runtime.InteropServices.WebAssembly.NetWasmPlatform(typeof({{name}}.PackagePlatform))]

        namespace {{name}};

        public sealed class PackagePlatform : System.Runtime.InteropServices.WebAssembly.NetWasmPlatform
        {
        }
        """;

    [Theory]
    [InlineData("typeof(Unrelated)", "'Bindings.Unrelated' does not derive from NetWasmPlatform")]
    [InlineData("typeof(AbstractPlatform)", "must be a non-generic, non-abstract class")]
    [InlineData("typeof(ValuePlatform)", "must be a non-generic, non-abstract class")]
    [InlineData("typeof(GenericPlatform<>)", "must be a non-generic, non-abstract class")]
    [InlineData("typeof(IClock)", "must be a non-generic, non-abstract class")]
    [InlineData("typeof(ConfiguredPlatform)", "has no public parameterless constructor")]
    [InlineData("typeof(PrivatePlatform)", "has no public parameterless constructor")]
    [InlineData("null", "must name a platform type")]
    [InlineData("typeof(TimedPlatform)", "provides Timers, but a program with WASI")]
    [InlineData("typeof(InheritsTimers)", "provides Timers, but a program with WASI")]
    public void InvalidPlatformsFailTheBuild(string argument, string message)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("Bindings", $$"""
            using System;
            using System.Runtime.InteropServices.WebAssembly;
            using Bindings;

            [assembly: NetWasmPlatform({{argument}})]

            namespace Bindings;

            public class Unrelated
            {
            }

            public abstract class AbstractPlatform : NetWasmPlatform
            {
            }

            public struct ValuePlatform
            {
            }

            public sealed class GenericPlatform<T> : NetWasmPlatform
            {
            }

            public sealed class ConfiguredPlatform(int seed) : NetWasmPlatform
            {
                public int Seed => seed;
            }

            public sealed class PrivatePlatform : NetWasmPlatform
            {
                private PrivatePlatform()
                {
                }
            }

            public class TimedPlatform : NetWasmPlatform
            {
                public override ITimerScheduler Timers => throw new NotSupportedException();
            }

            public sealed class InheritsTimers : TimedPlatform
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input) => DateTime.UtcNow.Year + input;
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "Bindings.EntryPoint", "Run", [])));

        Assert.Equal(DiagnosticCode.PlatformService, exception.Diagnostic.Code);
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void ResolverRejectsMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasm.Compiler.Analysis.PlatformServices.PlatformServiceBindingResolverFactory(null!));
    }
}
