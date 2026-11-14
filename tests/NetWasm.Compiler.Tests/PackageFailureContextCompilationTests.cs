using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PackageFailureContextCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.O3)]
    // At O3 allocation elimination removes the unused Derived, but the types a body constructed before managed
    // optimization stay required, so its base is still validated.
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.O3)]
    public void SealedReplacementRejectsReachedInheritanceButAllowsDirectUse(WasmTarget target, bool inherit, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Base.Dependency", """
            namespace Dependency;
            public class Base { public int Value => 42; }
            """);
        var library = assets.CompileSource("Derived.Library", """
            namespace Library;
            public sealed class Derived : Dependency.Base { }
            """, dependency);
        var app = assets.CompileSource("Derived.Application", $$"""
            namespace Application;
            public static class Entry { public static int Run(int input) => new {{(inherit ? "Library.Derived" : "Dependency.Base")}}().Value; }
            """, library, dependency);
        assets.CompileSource("Base.Dependency", """
            namespace Dependency;
            public sealed class Base { public int Value => 42; }
            """);
        var options = new CompilerOptions(app, [assets.CoreLib, library, dependency],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Derived.Library", "2.3.4", "lib/netstandard2.1/Derived.Library.dll")],
        };
        if (!inherit)
        {
            Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
            return;
        }
        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, failure.Diagnostic.Code);
        Assert.Equal(new AssemblyIdentity("Derived.Library"), failure.Diagnostic.Context!.Source.Assembly);
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
        Assert.Contains(failure.Diagnostic.DependencyChain, step => step.Kind == CompilerDependencyKind.BaseType);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.O3)]
    public void ResolvedTypesWithMissingMembersReportMissingApi(WasmTarget target, bool field, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Member.Dependency", """
            namespace Dependency;
            public static class Api { public static int Value = 42; public static int Read() => 42; }
            """);
        var library = assets.CompileSource("Member.Library", $$"""
            namespace Library;
            public static class Bridge { public static int Read() => {{(field ? "Dependency.Api.Value" : "Dependency.Api.Read()")}}; }
            """, dependency);
        var app = assets.CompileSource("Member.Application", """
            namespace Application;
            public static class Entry { public static int Run(int input) => Library.Bridge.Read(); }
            """, library);
        assets.CompileSource("Member.Dependency", """
            namespace Dependency;
            public static class Api { public static int Other() => 43; }
            """);
        var options = new CompilerOptions(app, [assets.CoreLib, library, dependency],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Member.Library", "2.3.4", "lib/netstandard2.1/Member.Library.dll")],
        };
        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        Assert.Equal(DiagnosticCode.MissingApi, failure.Diagnostic.Code);
        Assert.Equal("NW1016", failure.Diagnostic.Id);
        Assert.Equal(new AssemblyIdentity("Member.Library"), failure.Diagnostic.Context!.Source.Assembly);
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
        Assert.Equal(new AssemblyIdentity("Member.Application"),
            Assert.Single(failure.Diagnostic.DependencyChain).Source.Source.Assembly);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void ImportClassificationFailureRetainsPackageAndCaller(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("Native.Library", """
            namespace Library;
            public static class Native
            {
                [System.Runtime.InteropServices.DllImport("example", SetLastError = true)]
                public static extern int Read();
            }
            """);
        var app = assets.CompileSource("Native.Application", """
            namespace Application;
            public static class Entry { public static int Run(int input) => Library.Native.Read(); }
            """, library);
        var options = new CompilerOptions(app, [assets.CoreLib, library],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Native.Library", "2.3.4", "lib/netstandard2.1/Native.Library.dll")],
        };

        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Equal(DiagnosticCode.NativeInterop, failure.Diagnostic.Code);
        Assert.Equal(new AssemblyIdentity("Native.Library"), failure.Diagnostic.Context!.Source.Assembly);
        var caller = Assert.Single(failure.Diagnostic.DependencyChain);
        Assert.Equal(new AssemblyIdentity("Native.Application"), caller.Source.Source.Assembly);
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
        Assert.IsType<CompilerException>(failure.InnerException);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "dispatch", FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, "dispatch", FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, "dispatch", FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, "dispatch", FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, "initializer", FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, "initializer", FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, "initializer", FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, "initializer", FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, "delegate", FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, "delegate", FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, "delegate", FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, "delegate", FinalWasmOptimization.O3)]
    public void IndirectMethodRequirementsRetainTheirApplicationCaller(WasmTarget target, string scenario, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Indirect.Dependency", """
            namespace Dependency;
            public static class Api { public static int Read() => 42; }
            """);
        var librarySource = scenario switch
        {
            "dispatch" => """
                namespace Library;
                public interface IService { int Read(); }
                public sealed class Service : IService { public int Read() => Dependency.Api.Read(); }
                """,
            "initializer" => """
                namespace Library;
                public static class Service
                {
                    private static readonly int Value;
                    static Service() { Value = Dependency.Api.Read(); }
                    public static int Read() => Value;
                }
                """,
            _ => """
                namespace Library;
                public static class Service { public static int Read() => Dependency.Api.Read(); }
                """,
        };
        var invocation = scenario switch
        {
            "dispatch" => "Library.IService service = new Library.Service(); return service.Read();",
            "delegate" => "System.Func<int> read = Library.Service.Read; return read();",
            _ => "return Library.Service.Read();",
        };
        var library = assets.CompileSource("Indirect.Library", librarySource, dependency);
        var app = assets.CompileSource("Indirect.Application", $$"""
            namespace Application;
            public static class Entry { public static int Run(int input) { {{invocation}} } }
            """, library, dependency);
        var options = new CompilerOptions(app, [assets.CoreLib, library],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Indirect.Library", "2.3.4", "lib/netstandard2.1/Indirect.Library.dll")],
        };

        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Equal(DiagnosticCode.AssemblyResolution, failure.Diagnostic.Code);
        Assert.Equal(new AssemblyIdentity("Indirect.Library"), failure.Diagnostic.Context!.Source.Assembly);
        var step = Assert.Single(failure.Diagnostic.DependencyChain);
        Assert.Equal(new AssemblyIdentity("Indirect.Application"), step.Source.Source.Assembly);
        Assert.Equal(CompilerDependencyKind.MethodDependency, step.Kind);
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.O3)]
    public void RequiredFieldFailureIdentifiesItsLibrary(WasmTarget target, bool nested, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Field.Dependency", """
            namespace Dependency;
            public struct Payload { public long Number; }
            """);
        var library = assets.CompileSource("Field.Library", nested ? """
            namespace Library;
            public struct Cell { public Dependency.Payload Data; }
            public sealed class Value { public Cell Data; public int Number = 42; }
            """ : """
            namespace Library;
            public sealed class Value { public Dependency.Payload Data; public int Number = 42; }
            """, dependency);
        var app = assets.CompileSource("Field.Application", """
            namespace Application;
            public static class Entry { public static int Run(int input) => new Library.Value().Number; }
            """, library, dependency);
        var options = new CompilerOptions(app, [assets.CoreLib, library],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Field.Library", "2.3.4", "lib/netstandard2.1/Field.Library.dll")],
        };

        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Equal(DiagnosticCode.AssemblyResolution, failure.Diagnostic.Code);
        var context = Assert.IsType<CompilerDiagnosticContext>(failure.Diagnostic.Context);
        Assert.Equal(new AssemblyIdentity("Field.Library"), context.Source.Assembly);
        Assert.NotNull(context.DeclaringType);
        Assert.Equal(new AssemblyIdentity("Field.Application"),
            failure.Diagnostic.DependencyChain[0].Source.Source.Assembly);
        Assert.Equal(nested ? 3 : 2, failure.Diagnostic.DependencyChain.Length);
        Assert.All(failure.Diagnostic.DependencyChain.Skip(1), step =>
            Assert.Equal(CompilerDependencyKind.FieldStorage, step.Kind));
        Assert.Equal(0x04000000, context.Source.MetadataToken & unchecked((int)0xff000000));
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void RequiredInterfaceFailureIdentifiesItsLibraryType(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Structural.Dependency", """
            namespace Dependency;
            public interface IContract { }
            """);
        var library = assets.CompileSource("Structural.Library", """
            namespace Library;
            public sealed class Value : Dependency.IContract { public int Number = 42; }
            """, dependency);
        var app = assets.CompileSource("Structural.Application", """
            namespace Application;
            public static class Entry { public static int Run(int input) => new Library.Value().Number; }
            """, library, dependency);
        var options = new CompilerOptions(app, [assets.CoreLib, library],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Structural.Library", "2.3.4", "lib/netstandard2.1/Structural.Library.dll")],
        };

        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Equal(DiagnosticCode.AssemblyResolution, failure.Diagnostic.Code);
        var context = Assert.IsType<CompilerDiagnosticContext>(failure.Diagnostic.Context);
        Assert.Equal(new AssemblyIdentity("Structural.Library"), context.Source.Assembly);
        Assert.Equal(new AssemblyIdentity("Structural.Application"),
            failure.Diagnostic.DependencyChain[0].Source.Source.Assembly);
        Assert.Contains(failure.Diagnostic.DependencyChain, step =>
            step.Kind == CompilerDependencyKind.Interface && step.Source.Source == context.Source);
        Assert.Equal(0x02000000, context.Source.MetadataToken & unchecked((int)0xff000000));
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
        Assert.IsType<CompilerException>(failure.InnerException);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void RequiredDependencyFailureIdentifiesItsLibraryMethod(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Origin.Dependency", """
            namespace Dependency;
            public static class Api { public static int Read() => 42; }
            """);
        var library = assets.CompileSource("Origin.Library", """
            namespace Library;
            public static class Service<T> { public static int Read() => Bridge(); public static int Bridge() => Dependency.Api.Read(); }
            """, dependency);
        var app = assets.CompileSource("Origin.Application", """
            namespace Application;
            public static class Entry { public static int Run(int input) => Library.Service<int>.Read(); }
            """, library, dependency);
        var options = new CompilerOptions(app, [assets.CoreLib, library],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Example.Library", "2.3.4", "lib/netstandard2.1/Origin.Library.dll")],
        };

        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Equal(DiagnosticCode.AssemblyResolution, failure.Diagnostic.Code);
        var context = Assert.IsType<CompilerDiagnosticContext>(failure.Diagnostic.Context);
        Assert.Equal(new AssemblyIdentity("Origin.Library"), context.Source.Assembly);
        if (optimization == FinalWasmOptimization.None)
        {
            Assert.Collection(failure.Diagnostic.DependencyChain,
                step => Assert.Equal(new AssemblyIdentity("Origin.Application"), step.Source.Source.Assembly),
                step =>
                {
                    Assert.Equal(new AssemblyIdentity("Origin.Library"), step.Source.Source.Assembly);
                    Assert.Equal(0x06000001, step.Source.Source.MetadataToken);
                });
        }
        else
        {
            // Inlining folds Service<int>.Read into Run, which then reaches Bridge directly.
            Assert.Equal(new AssemblyIdentity("Origin.Application"),
                Assert.Single(failure.Diagnostic.DependencyChain).Source.Source.Assembly);
        }
        Assert.Equal(0x06000000, context.Source.MetadataToken & unchecked((int)0xff000000));
        Assert.Contains("Library.Service", context.InstanceIdentity, StringComparison.Ordinal);
        Assert.Contains("primitive:i4", context.InstanceIdentity, StringComparison.Ordinal);
        Assert.IsType<CompilerException>(failure.InnerException);
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
        Assert.Contains("Example.Library 2.3.4", failure.Message, StringComparison.Ordinal);
        Assert.Contains("lib/netstandard2.1/Origin.Library.dll", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(assets.Directory, failure.Message, StringComparison.Ordinal);
    }

    // A static readonly field's load folds to the constant its owner's static constructor stores,
    // so the compiler reads that constructor while compiling the load. A constructor that
    // references a missing assembly is the library's failure, not the caller's.
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void StaticInitializerFailureIdentifiesItsLibrary(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Initializer.Dependency", """
            namespace Dependency;
            public static class Api { public static int Read() => 42; }
            """);
        var library = assets.CompileSource("Initializer.Library", """
            namespace Library;
            public static class Settings { public static readonly int Limit = Dependency.Api.Read(); }
            """, dependency);
        var app = assets.CompileSource("Initializer.Application", """
            namespace Application;
            public static class Entry { public static int Run(int input) => Library.Settings.Limit; }
            """, library);
        var options = new CompilerOptions(app, [assets.CoreLib, library],
            "Application.Entry", "Run", [], target, Optimization: optimization)
        {
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Initializer.Library", "2.3.4", "lib/netstandard2.1/Initializer.Library.dll")],
        };

        var failure = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Equal(DiagnosticCode.AssemblyResolution, failure.Diagnostic.Code);
        var context = Assert.IsType<CompilerDiagnosticContext>(failure.Diagnostic.Context);
        Assert.Equal(new AssemblyIdentity("Initializer.Library"), context.Source.Assembly);
        Assert.Equal(0x06000000, context.Source.MetadataToken & unchecked((int)0xff000000));
        Assert.Equal(options.ReferenceOrigins[0], failure.Diagnostic.Origin);
    }
}
