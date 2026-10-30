using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class EntryPointContractCoverageTests
{
    private static readonly AssemblyIdentity Assembly = new("EntryPointCoverage");
    private static readonly EntityKey Type = new(Assembly, 0x02000001);

    [Fact]
    public void EntryPointSelectorValidatesItsDependenciesAndInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new EntryPointSelector(null!));
        var selector = new EntryPointSelector(CreateValidator());
        var metadata = Snapshot([Method("Main", 1, true, CliValueKind.I4, [CliValueKind.I4])]);
        var methods = new MethodFinder(metadata.Methods);
        var symbols = new Symbols();
        var options = Options();

        Assert.Throws<ArgumentNullException>(() => selector.Select(
            null!, methods, symbols, options));
        Assert.Throws<ArgumentNullException>(() => selector.Select(
            metadata, null!, symbols, options));
        Assert.Throws<ArgumentNullException>(() => selector.Select(
            metadata, methods, null!, options));
        Assert.Throws<ArgumentNullException>(() => selector.Select(
            metadata, methods, symbols, null!));
        Assert.Equal("Main", selector.Select(metadata, methods, symbols, options).Name);

        var tokenOptions = options with { EntryMethodToken = 0x06000001 };
        Assert.Equal(
            "Main",
            selector.Select(metadata, methods, symbols, tokenOptions).Name);
    }

    [Theory]
    [InlineData(false, CliValueKind.I4, CliValueKind.I4)]
    [InlineData(true, CliValueKind.Void, CliValueKind.I4)]
    [InlineData(true, CliValueKind.I4, CliValueKind.Void)]
    public void EntryPointValidatorRejectsEveryInvalidSignature(
        bool isStatic,
        CliValueKind result,
        CliValueKind parameter)
    {
        var validator = CreateValidator();
        var method = Method("Main", 1, isStatic, result, [parameter]);
        var symbols = new Symbols();

        var exception = Assert.Throws<CompilerException>(() => validator.Validate(
            method,
            symbols,
            CompilerEntryPointKind.RawFunction));

        Assert.Equal(DiagnosticCode.InvalidEntryPoint, exception.Diagnostic.Code);
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            null!, symbols, CompilerEntryPointKind.RawFunction));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            method, null!, CompilerEntryPointKind.RawFunction));
    }

    [Fact]
    public void ManagedExecutableValidatorAcceptsStandardSynchronousMainShapes()
    {
        var validator = CreateValidator();
        var symbols = new Symbols();
        var stringType = CliTypeIdentity.Named(
            new AssemblyIdentity("System.Private.CoreLib"),
            "System",
            "String",
            isValueType: false);
        var stringArray = CliTypeIdentity.SzArray(stringType);
        var primitiveStringArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false));
        var tasks = new AssemblyIdentity("System.Private.CoreLib");
        var task = CliTypeIdentity.Named(
            tasks,
            "System.Threading.Tasks",
            "Task",
            isValueType: false);
        var genericTask = CliTypeIdentity.Named(
            tasks,
            "System.Threading.Tasks",
            "Task`1",
            isValueType: false);
        var taskOfInt = CliTypeIdentity.GenericInstantiation(
            genericTask,
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);

        foreach (var method in new[]
                 {
                     Method("Main", 1, true, CliValueKind.Void, []),
                     Method("Main", 2, true, CliValueKind.I4, []),
                     Method("Main", 3, true, CliValueKind.Void, []) with
                     {
                         Signature = MethodSignatureModel.Create(
                             CliTypeIdentity.FromStackKind(CliValueKind.Void),
                             stringArray),
                     },
                     Method("Main", 4, true, CliValueKind.I4, []) with
                     {
                         Signature = MethodSignatureModel.Create(
                             CliTypeIdentity.FromStackKind(CliValueKind.I4),
                             stringArray),
                     },
                     Method("Main", 3, true, CliValueKind.Void, []) with
                     {
                         Signature = MethodSignatureModel.Create(
                             CliTypeIdentity.FromStackKind(CliValueKind.Void),
                             primitiveStringArray),
                     },
                     Method("Main", 4, true, CliValueKind.I4, []) with
                     {
                         Signature = MethodSignatureModel.Create(
                             CliTypeIdentity.FromStackKind(CliValueKind.I4),
                             primitiveStringArray),
                     },
                     Method("Main", 5, true, CliValueKind.ManagedReference, []) with
                     {
                         Signature = MethodSignatureModel.Create(task),
                     },
                     Method("Main", 6, true, CliValueKind.ManagedReference, []) with
                     {
                         Signature = MethodSignatureModel.Create(
                             taskOfInt,
                             stringArray),
                     },
                 })
        {
            validator.Validate(
                method,
                symbols,
                CompilerEntryPointKind.ManagedExecutable);
        }
    }

    [Fact]
    public void ManagedExecutableValidatorRejectsUnsupportedMainShapes()
    {
        var validator = CreateValidator();
        var symbols = new Symbols();
        var objectType = CliTypeIdentity.Named(
            new AssemblyIdentity("System.Private.CoreLib"),
            "System",
            "Object",
            isValueType: false);
        var tasks = new AssemblyIdentity("System.Private.CoreLib");
        var taskOfString = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                tasks,
                "System.Threading.Tasks",
                "Task`1",
                isValueType: false),
            [CliTypeIdentity.Primitive(
                "string",
                CliValueKind.ManagedReference,
                isValueType: false)]);
        var valueTaskOfInt = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                tasks,
                "System.Threading.Tasks",
                "ValueTask`1",
                isValueType: true),
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var invalid = new[]
        {
            Method("Main", 1, false, CliValueKind.Void, []),
            Method("Main", 2, true, CliValueKind.I8, []),
            Method("Main", 3, true, CliValueKind.Void, [CliValueKind.I4]),
            Method("Main", 3, true, CliValueKind.Void, [CliValueKind.I4, CliValueKind.I4]),
            Method("Main", 4, true, CliValueKind.Void, []) with
            {
                GenericArity = 1,
            },
            Method("Main", 5, true, CliValueKind.Void, []) with
            {
                Signature = MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    CliTypeIdentity.SzArray(objectType)),
            },
            Method("Main", 6, true, CliValueKind.ManagedReference, []) with
            {
                Signature = MethodSignatureModel.Create(taskOfString),
            },
            Method("Main", 7, true, CliValueKind.ValueType, []) with
            {
                Signature = MethodSignatureModel.Create(valueTaskOfInt),
            },
        };

        Assert.All(invalid, method => Assert.Throws<CompilerException>(() =>
            validator.Validate(
                method,
                symbols,
                CompilerEntryPointKind.ManagedExecutable)));
    }

    [Fact]
    public void EntryPointValidationRegistryRejectsMissingAndDuplicateStrategies()
    {
        Assert.Throws<ArgumentNullException>(() => new EntryPointValidator(null!));
        Assert.Throws<InvalidOperationException>(() => new EntryPointValidator([]));
        Assert.Throws<InvalidOperationException>(() => new EntryPointValidator(
            [
                new RawFunctionEntryPointValidationStrategy(),
                new RawFunctionEntryPointValidationStrategy(),
                new ManagedExecutableEntryPointValidationStrategy(),
            ]));
    }

    [Fact]
    public void ExportNameValidatorReportsTheCandidateThatDuplicatesAnEarlierName()
    {
        var validator = new CompilationExportNameValidator();
        var method = Method("Main", 1, true, CliValueKind.I4, [CliValueKind.I4]);
        var candidates = new[]
        {
            new CompilationExportCandidate(
                new ProgramExport("duplicate", method.Key),
                DiagnosticCode.InvalidCommandLine,
                "duplicate export",
                null),
            new CompilationExportCandidate(
                new ProgramExport("duplicate", method.Key),
                DiagnosticCode.ComponentContract,
                "duplicate component export",
                method),
        };

        var exception = Assert.Throws<CompilerException>(() => validator.Validate(
            new Symbols(),
            candidates));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Equal("Main", exception.Diagnostic.Method);

        var nullMethodCandidates = new[]
        {
            new CompilationExportCandidate(
                new ProgramExport("duplicate", method.Key),
                DiagnosticCode.InvalidCommandLine,
                "duplicate export",
                null),
            new CompilationExportCandidate(
                new ProgramExport("duplicate", method.Key),
                DiagnosticCode.ComponentContract,
                "duplicate component export",
                null),
        };
        var nullMethodException = Assert.Throws<CompilerException>(() =>
            validator.Validate(new Symbols(), nullMethodCandidates));
        Assert.Null(nullMethodException.Diagnostic.Method);
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, candidates));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(new Symbols(), null!));
    }

    [Fact]
    public void CompilationExportResolverRequiresContributorsAndValidatesEachAddition()
    {
        var names = new CompilationExportNameValidator();
        Assert.Throws<ArgumentNullException>(() => new CompilationExportResolver(null!, names));
        Assert.Throws<ArgumentNullException>(() => new CompilationExportResolver([], null!));
        Assert.Throws<InvalidOperationException>(() => new CompilationExportResolver([], names));

        var method = Method("Main", 1, true, CliValueKind.I4, [CliValueKind.I4]);
        var metadata = Snapshot([method]);
        var resolver = new CompilationExportResolver(
            [new FixedExportResolver(
                [new CompilationExportCandidate(
                    new ProgramExport("main", method.Key),
                    DiagnosticCode.InvalidCommandLine,
                    "duplicate",
                    null)])],
            names);

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            null!, new MethodFinder(metadata.Methods), new Symbols(), Options()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            metadata, null!, new Symbols(), Options()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            metadata, new MethodFinder(metadata.Methods), null!, Options()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            metadata, new MethodFinder(metadata.Methods), new Symbols(), null!));
        Assert.Equal("main", Assert.Single(resolver.Resolve(
            metadata,
            new MethodFinder(metadata.Methods),
            new Symbols(),
            Options())).Name);
    }

    [Fact]
    public void RequestedExportsRejectNullInputsAndDuplicateNames()
    {
        var method = Method("Main", 1, true, CliValueKind.I4, [CliValueKind.I4]);
        var metadata = Snapshot([method]);
        var resolver = new RequestedExportResolver();
        var finder = new MethodFinder(metadata.Methods);
        var symbols = new Symbols();

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            null!, finder, Options()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            metadata, null!, Options()));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(
            metadata, finder, null!));
        var options = Options() with
        {
            Exports = [
                new RequestedExport("main", "Program", "Main"),
                new RequestedExport("main", "Program", "Main"),
            ],
        };
        Assert.Throws<CompilerException>(() => resolver.Resolve(metadata, finder, options));
        _ = symbols;
    }

    [Fact]
    public void AnnotatedExportResolversFilterAndSortTheirOwnDeclarations()
    {
        var first = Method("First", 1, true, CliValueKind.Void, []);
        var second = Method("Second", 2, true, CliValueKind.Void, []);
        var third = Method("Third", 3, true, CliValueKind.Void, []);
        first = first with { JSExport = new InteropExportDeclaration("z") };
        second = second with
        {
            WitExport = new WitExportDeclaration("test:pkg/interface@1.0.0", "z"),
            WitPostReturn = new WitPostReturnDeclaration(
                "test:pkg/interface@1.0.0",
                "z"),
        };
        third = third with { JSExport = new InteropExportDeclaration(null) };
        var metadata = Snapshot([first, second, third]);
        var finder = new MethodFinder(metadata.Methods);
        var options = Options();

        var js = new JavaScriptExportResolver();
        Assert.Equal(["Third", "z"], js.Resolve(metadata, finder, options)
            .Select(candidate => candidate.Export.Name));
        var componentOptions = options with { WitPath = "selected-world.wit" };
        Assert.Empty(js.Resolve(metadata, finder, componentOptions));
        Assert.Equal(["Third", "z"], js.Resolve(metadata, finder,
                componentOptions with { UseJavaScriptExportBoundary = true })
            .Select(candidate => candidate.Export.Name));
        Assert.Equal(["Third", "z"], js.Resolve(metadata, finder,
                options with { UseJavaScriptExportBoundary = true })
            .Select(candidate => candidate.Export.Name));
        var wit = new WitExportResolver();
        Assert.Equal(["cm32p2|test:pkg/interface@1|z"],
            wit.Resolve(metadata, finder, options)
            .Select(candidate => candidate.Export.Name));
        var post = new WitPostReturnExportResolver();
        Assert.Equal(["cm32p2|test:pkg/interface@1|z_post"],
            post.Resolve(metadata, finder, options)
            .Select(candidate => candidate.Export.Name));

        Assert.Throws<ArgumentNullException>(() => js.Resolve(null!, finder, options));
        Assert.Throws<ArgumentNullException>(() => js.Resolve(metadata, null!, options));
        Assert.Throws<ArgumentNullException>(() => js.Resolve(metadata, finder, null!));
        Assert.Throws<ArgumentNullException>(() => wit.Resolve(null!, finder, options));
        Assert.Throws<ArgumentNullException>(() => post.Resolve(metadata, finder, null!));
    }

    [Fact]
    public void CompilationEntryResolverDelegatesEntryAndExportResolution()
    {
        var method = Method("Main", 1, true, CliValueKind.I4, [CliValueKind.I4]);
        var metadata = Snapshot([method]);
        var entry = new FixedEntrySelector(method);
        var exports = new FixedExportsResolver([new ProgramExport("main", method.Key)]);
        var resolver = new CompilationEntryResolver(entry, exports);

        var result = resolver.Resolve(
            metadata,
            new MethodFinder(metadata.Methods),
            new Symbols(),
            Options());

        Assert.Same(method, result.EntryPoint);
        Assert.Equal("main", Assert.Single(result.Exports).Name);
        Assert.Throws<ArgumentNullException>(() => new CompilationEntryResolver(null!, exports));
        Assert.Throws<ArgumentNullException>(() => new CompilationEntryResolver(entry, null!));
    }

    [Fact]
    public void LibraryEntryResolutionPreservesExportsWithoutSelectingAnEntry()
    {
        var method = Method("Increment", 1, true, CliValueKind.I8, [CliValueKind.I8]);
        var metadata = Snapshot([method]);
        var entry = new FixedEntrySelector(method);
        var exports = new FixedExportsResolver([new ProgramExport("increment", method.Key)]);
        var resolver = new[] { new CompilationEntryResolver(entry, exports) }
            .Cast<ICompilationEntryResolver>().Single();

        var result = resolver.Resolve(metadata, new MethodFinder(metadata.Methods),
            new Symbols(), Options() with { EntryPointKind = CompilerEntryPointKind.Library });

        Assert.Null(result.EntryPoint);
        Assert.Equal(0, entry.Calls);
        Assert.Equal(new ProgramExport("increment", method.Key), Assert.Single(result.Exports));
    }

    [Fact]
    public void ManagedStringArrayEntrySelectsTheCoreLibArgumentFactory()
    {
        var coreLib = new AssemblyIdentity("System.Private.CoreLib");
        var platformTypeKey = new EntityKey(coreLib, 0x02000001);
        var factoryKey = new EntityKey(coreLib, 0x06000001);
        var stringType = CliTypeIdentity.Named(
            coreLib,
            "System",
            "String",
            isValueType: false);
        var stringArray = CliTypeIdentity.SzArray(stringType);
        var factory = new MethodDefinitionModel(
            factoryKey,
            platformTypeKey,
            "ReadManagedEntryPointArguments",
            true,
            MethodSignatureModel.Create(stringArray),
            0);
        var platformType = new TypeDefinitionModel(
            platformTypeKey,
            "System.Runtime.InteropServices",
            "PlatformServices",
            false,
            [],
            [factoryKey]);
        var repository = new ArgumentFactoryRepository(platformType, factory);
        var entry = Method("Main", 1, true, CliValueKind.Void, []) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                stringArray),
        };
        var resolver = new ManagedExecutableArgumentFactoryResolver();

        Assert.Equal(factoryKey, resolver.Resolve(
            CompilerEntryPointKind.ManagedExecutable,
            entry,
            repository,
            repository));
        Assert.Null(resolver.Resolve(
            CompilerEntryPointKind.RawFunction,
            entry,
            repository,
            repository));
        Assert.Null(resolver.Resolve(
            CompilerEntryPointKind.ManagedExecutable,
            Method("Main", 2, true, CliValueKind.Void, []),
            repository,
            repository));
    }

    [Fact]
    public void ManagedStringArrayEntryRequiresExactlyOneArgumentFactory()
    {
        var coreLib = new AssemblyIdentity("System.Private.CoreLib");
        var platformTypeKey = new EntityKey(coreLib, 0x02000001);
        var stringArray = CliTypeIdentity.SzArray(CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false));
        var entry = Method("Main", 1, true, CliValueKind.Void, []) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                stringArray),
        };
        var resolver = new ManagedExecutableArgumentFactoryResolver();
        var emptyType = new TypeDefinitionModel(
            platformTypeKey,
            "System.Runtime.InteropServices",
            "PlatformServices",
            false,
            [],
            []);
        var empty = new ArgumentFactoryRepository(emptyType);

        Assert.Throws<CompilerException>(() => resolver.Resolve(
            CompilerEntryPointKind.ManagedExecutable,
            entry,
            empty,
            empty));

        var first = ArgumentFactory(coreLib, platformTypeKey, 1, stringArray);
        var second = ArgumentFactory(coreLib, platformTypeKey, 2, stringArray);
        var ambiguousType = emptyType with { Methods = [first.Key, second.Key] };
        var ambiguous = new ArgumentFactoryRepository(ambiguousType, first, second);
        Assert.Throws<CompilerException>(() => resolver.Resolve(
            CompilerEntryPointKind.ManagedExecutable,
            entry,
            ambiguous,
            ambiguous));
    }

    [Fact]
    public void ArgumentFactoryCandidateMustMatchTheEntireContract()
    {
        var coreLib = new AssemblyIdentity("System.Private.CoreLib");
        var platformTypeKey = new EntityKey(coreLib, 0x02000001);
        var stringArray = CliTypeIdentity.SzArray(CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false));
        var wrongElementArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var candidates = new[]
        {
            ArgumentFactory(coreLib, platformTypeKey, 1, stringArray) with
            {
                Name = "Other",
            },
            ArgumentFactory(coreLib, platformTypeKey, 2, stringArray) with
            {
                IsStatic = false,
            },
            ArgumentFactory(coreLib, platformTypeKey, 3, stringArray) with
            {
                Signature = MethodSignatureModel.Create(
                    stringArray,
                    CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            },
            ArgumentFactory(
                coreLib,
                platformTypeKey,
                4,
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            ArgumentFactory(coreLib, platformTypeKey, 5, wrongElementArray),
        };
        var platformType = new TypeDefinitionModel(
            platformTypeKey,
            "System.Runtime.InteropServices",
            "PlatformServices",
            false,
            [],
            [.. candidates.Select(candidate => candidate.Key)]);
        var repository = new ArgumentFactoryRepository(platformType, candidates);
        var resolver = new ManagedExecutableArgumentFactoryResolver();
        var entry = Method("Main", 6, true, CliValueKind.Void, []) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                stringArray),
        };

        Assert.Throws<CompilerException>(() => resolver.Resolve(
            CompilerEntryPointKind.ManagedExecutable,
            entry,
            repository,
            repository));
    }

    [Fact]
    public void EntryPointRegistrationRejectsNullAndExposesTheEntryCapability()
    {
        IServiceCollection? missing = null;
        Assert.Throws<ArgumentNullException>(() => missing!.AddCompilerEntryPoints());

        var services = new ServiceCollection();
        services.AddCompilerEntryPoints();
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ICompilationEntryResolver>());
        Assert.NotNull(provider.GetRequiredService<
            IManagedExecutableArgumentFactoryResolver>());
    }

    private static CompilerOptions Options() => new(
        "entry.dll",
        [],
        "Program",
        "Main",
        []);

    private static EntryPointValidator CreateValidator() => new(
        [
            new RawFunctionEntryPointValidationStrategy(),
            new ManagedExecutableEntryPointValidationStrategy(),
        ]);

    private static MetadataCompilationSnapshot Snapshot(
        ImmutableArray<MethodDefinitionModel> methods) => new(
        [],
        Assembly,
        [new TypeDefinitionModel(Type, "", "Program", false, [], [])],
        methods,
        methods);

    private static MethodDefinitionModel Method(
        string name,
        int token,
        bool isStatic,
        CliValueKind result,
        ImmutableArray<CliValueKind> parameters) => new(
        new EntityKey(Assembly, 0x06000000 + token),
        Type,
        name,
        isStatic,
        MethodSignatureModel.Create(result, parameters.ToArray()),
        0);

    private static MethodDefinitionModel ArgumentFactory(
        AssemblyIdentity assembly,
        EntityKey declaringType,
        int token,
        CliTypeIdentity result) => new(
        new EntityKey(assembly, 0x06000000 + token),
        declaringType,
        "ReadManagedEntryPointArguments",
        true,
        MethodSignatureModel.Create(result),
        0);

    private sealed class MethodFinder(ImmutableArray<MethodDefinitionModel> methods) : IMethodFinder
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

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();
        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class ArgumentFactoryRepository(
        TypeDefinitionModel type,
        params MethodDefinitionModel[] methods) : ITypeFinder, IMethodRepository
    {
        public TypeDefinitionModel FindType(string fullName)
        {
            Assert.Equal(type.FullName, fullName);
            return type;
        }

        public MethodDefinitionModel GetMethod(EntityKey key)
        {
            return Assert.Single(methods, method => method.Key == key);
        }
    }

    private sealed class FixedExportResolver(
        IReadOnlyList<CompilationExportCandidate> exports) : ICompilationExportResolver
    {
        public IReadOnlyList<CompilationExportCandidate> Resolve(
            MetadataCompilationSnapshot metadata,
            IMethodFinder methods,
            CompilerOptions options) => exports;
    }

    private sealed class FixedEntrySelector(MethodDefinitionModel method) : IEntryPointSelector
    {
        public int Calls { get; private set; }

        public MethodDefinitionModel Select(
            MetadataCompilationSnapshot metadata,
            IMethodFinder methods,
            ISymbolFormatter symbols,
            CompilerOptions options)
        {
            Calls++;
            return method;
        }
    }

    private sealed class FixedExportsResolver(ImmutableArray<ProgramExport> exports) :
        ICompilationExportsResolver
    {
        public ImmutableArray<ProgramExport> Resolve(
            MetadataCompilationSnapshot metadata,
            IMethodFinder methods,
            ISymbolFormatter symbols,
            CompilerOptions options) => exports;
    }
}
