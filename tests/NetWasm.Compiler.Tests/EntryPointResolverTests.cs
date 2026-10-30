using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class EntryPointResolverTests
{
    [Fact]
    public void ResolverCombinesAnnotatedExportsInStableOrder()
    {
        var assembly = new AssemblyIdentity("Application");
        var declaringType = new EntityKey(assembly, 1);
        var entryPoint = Method(declaringType, 2, "Main", isStatic: true)
            with
        { Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4) };
        var javaScript = Method(declaringType, 3, "Zed", isStatic: true)
            with
        { JSExport = new InteropExportDeclaration(null) };
        var wit = Method(declaringType, 4, "Wit", isStatic: true)
            with
        {
            WitExport = new WitExportDeclaration(
                    "test:pkg/interface@1.0.0",
                    "run"),
        };
        var postReturn = Method(declaringType, 5, "PostReturn", isStatic: true)
            with
        {
            WitPostReturn = new WitPostReturnDeclaration(
                    "test:pkg/interface@1.0.0",
                    "run"),
        };
        var methods = ImmutableArray.Create(entryPoint, javaScript, wit, postReturn);
        var resolver = CreateResolver();

        var result = resolver.Resolve(
            Snapshot(assembly, methods),
            new MethodFinder(methods),
            new SymbolFormatter(),
            new CompilerOptions("entry.dll", [], "Program", "Main", []));

        Assert.Equal(
            [
                "Zed",
                "cm32p2|test:pkg/interface@1|run",
                "cm32p2|test:pkg/interface@1|run_post",
            ],
            result.Exports.Select(export => export.Name));
        Assert.Equal(entryPoint, result.EntryPoint);
    }

    [Fact]
    public void ResolverExportsAnnotatedMethodsFromReferencedAssemblies()
    {
        var application = new AssemblyIdentity("Application");
        var library = new AssemblyIdentity("Library");
        var applicationType = new EntityKey(application, 1);
        var libraryType = new EntityKey(library, 1);
        var entryPoint = Method(applicationType, 2, "Main", isStatic: true)
            with
        { Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4) };
        var javaScript = Method(libraryType, 3, "LibraryCallback", isStatic: true)
            with
        { JSExport = new InteropExportDeclaration("libraryCallback") };
        var methods = ImmutableArray.Create(entryPoint, javaScript);

        var result = CreateResolver().Resolve(
            Snapshot(application, methods, [entryPoint]),
            new MethodFinder(methods),
            new SymbolFormatter(),
            new CompilerOptions("entry.dll", [], "Program", "Main", []));

        Assert.Equal(["libraryCallback"], result.Exports.Select(export => export.Name));
    }

    [Fact]
    public void ResolverNamesBothAssembliesForAnExportDeclaredTwice()
    {
        var application = new AssemblyIdentity("Application");
        var library = new AssemblyIdentity("Library");
        var applicationType = new EntityKey(application, 1);
        var entryPoint = Method(applicationType, 2, "Main", isStatic: true)
            with
        { Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4) };
        var mine = Method(applicationType, 3, "Resize", isStatic: true)
            with
        { JSExport = new InteropExportDeclaration("resize") };
        var theirs = Method(new EntityKey(library, 1), 3, "OnResize", isStatic: true)
            with
        { JSExport = new InteropExportDeclaration("resize") };
        var methods = ImmutableArray.Create(entryPoint, mine, theirs);

        var error = Assert.Throws<CompilerException>(() => CreateResolver().Resolve(
            Snapshot(application, methods, [entryPoint, mine]),
            new MethodFinder(methods),
            new SymbolFormatter(),
            new CompilerOptions("entry.dll", [], "Program", "Main", [])));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, error.Diagnostic.Code);
        Assert.Contains("duplicate Wasm export name 'resize'", error.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Resize in Application", error.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("OnResize in Library", error.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverRejectsDuplicateRequestedExportNamesBeforeMethodLookup()
    {
        var assembly = new AssemblyIdentity("Application");
        var declaringType = new EntityKey(assembly, 1);
        var entryPoint = Method(declaringType, 2, "Main", isStatic: true)
            with
        { Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4) };
        var methods = ImmutableArray.Create(entryPoint);
        var resolver = CreateResolver();
        var options = new CompilerOptions(
            "entry.dll",
            [],
            "Program",
            "Main",
            [
                new RequestedExport("same", "Program", "Main"),
                new RequestedExport("same", "Program", "AlsoMissing"),
            ]);

        var error = Assert.Throws<CompilerException>(() =>
            resolver.Resolve(
                Snapshot(assembly, methods),
                new MethodFinder(methods),
                new SymbolFormatter(),
                options));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, error.Diagnostic.Code);
        Assert.Contains("duplicate requested export name 'same'", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverRejectsAnEntryPointWithTheWrongSignature()
    {
        var assembly = new AssemblyIdentity("Application");
        var declaringType = new EntityKey(assembly, 1);
        var invalid = Method(declaringType, 2, "Main", isStatic: false);
        var resolver = CreateResolver();

        var error = Assert.Throws<CompilerException>(() => resolver.Resolve(
            Snapshot(assembly, [invalid]),
            new MethodFinder([invalid]),
            new SymbolFormatter(),
            new CompilerOptions("entry.dll", [], "Program", "Main", [])));

        Assert.Equal(DiagnosticCode.InvalidEntryPoint, error.Diagnostic.Code);
    }

    private static CompilationEntryResolver CreateResolver()
    {
        var validator = new EntryPointValidator(
            [
                new RawFunctionEntryPointValidationStrategy(),
                new ManagedExecutableEntryPointValidationStrategy(),
            ]);
        return new(
            new EntryPointSelector(validator),
            new CompilationExportResolver(
                [
                    new RequestedExportResolver(),
                    new JavaScriptExportResolver(),
                    new WitExportResolver(),
                    new WitPostReturnExportResolver(),
                ],
                new CompilationExportNameValidator()));
    }

    private static MethodDefinitionModel Method(
        EntityKey declaringType,
        int token,
        string name,
        bool isStatic) => new(
            new EntityKey(declaringType.Assembly, token),
            declaringType,
            name,
            isStatic,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);

    private static MetadataCompilationSnapshot Snapshot(
        AssemblyIdentity assembly,
        ImmutableArray<MethodDefinitionModel> methods) => Snapshot(assembly, methods, methods);

    private static MetadataCompilationSnapshot Snapshot(
        AssemblyIdentity assembly,
        ImmutableArray<MethodDefinitionModel> methods,
        ImmutableArray<MethodDefinitionModel> entryAssemblyMethods) => new(
        [],
        assembly,
        [],
        methods,
        entryAssemblyMethods);

    private sealed class MethodFinder(
        ImmutableArray<MethodDefinitionModel> methods) : IMethodFinder
    {
        public MethodDefinitionModel FindMethod(
            AssemblyIdentity assembly,
            string typeFullName,
            string methodName) => methods.Single(method => method.Name == methodName);

        public MethodDefinitionModel FindMethod(
            AssemblyIdentity assembly,
            int metadataToken) => methods.Single(method =>
                method.Key.MetadataToken == metadataToken);
    }

    private sealed class SymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }
}
