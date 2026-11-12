using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests;

public sealed class PipelineActorTests
{
    [Fact]
    public void PipelineActorsRejectEveryNullDependency()
    {
        Type[] actorTypes =
        [
            typeof(CompilationAnalysis),
            typeof(CompilationAnalysisBuilder),
            typeof(CompilationComplexityStage),
            typeof(CompilationDiagnosticTraceStage),
            typeof(CompilationEmissionBuilder),
            typeof(CompilationLayoutBuilder),
            typeof(CompilationLayouts),
            typeof(CompilationMetadataStage),
            typeof(CompilationPipelineExecutor),
            typeof(CompilationPreparation),
            typeof(CompilationPreparationBuilder),
            typeof(CompilationRootMapBuilder),
        ];

        foreach (var actorType in actorTypes)
        {
            var constructor = actorType.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single();
            var parameters = constructor.GetParameters();
            for (var index = 0; index < parameters.Length; index++)
            {
                if (parameters[index].ParameterType.IsValueType)
                {
                    continue;
                }

                var arguments = parameters
                    .Select(parameter => NullDependency(parameter.ParameterType))
                    .ToArray();
                arguments[index] = null!;
                var exception = Assert.Throws<TargetInvocationException>(() =>
                    constructor.Invoke(arguments));
                Assert.IsType<ArgumentNullException>(exception.InnerException);
            }
        }
    }

    [Fact]
    public void ExportMergerAddsNewBindingsAndRetainsEquivalentBindings()
    {
        var method = ValidationTestData.Program().EntryPoint!;
        var function = Function(method.Key);
        var contract = new ComponentBoundaryContract("", "", [], [function]);
        var merger = new ComponentExportMerger();

        var original = ImmutableArray.Create(new ProgramExport("existing", method.Key));
        Assert.Equal(
            original,
            merger.Merge(original, ComponentBoundaryContract.Empty, WasmTarget.Wasm32));

        var added = merger.Merge([], contract, WasmTarget.Wasm32);
        var retained = merger.Merge(added, contract, WasmTarget.Wasm32);

        Assert.Equal("cm32p2||run", added[0].Name);
        Assert.Equal(added.ToArray(), retained.ToArray());
    }

    [Fact]
    public void ExportMergerRejectsConflictingBindings()
    {
        var program = ValidationTestData.Program();
        var first = program.EntryPoint!;
        var second = first with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000002),
        };
        var contract = new ComponentBoundaryContract(
            "",
            "",
            [],
            [Function(second.Key)]);

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentExportMerger().Merge(
                [new ProgramExport("cm32p2||run", first.Key)],
                contract,
                WasmTarget.Wasm32));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void ExportMergerReplacesSemanticNamesWithSelectedWorldAliases()
    {
        var method = ValidationTestData.Program().EntryPoint!.Key;
        var postReturn = new EntityKey(ValidationTestData.Assembly, 0x06000002);
        var function = new CanonicalAbiFunction(
            "example:worker/math-api@1.0.0",
            "add-value",
            method,
            [],
            null)
        {
            CoreInterfaceName = "math-alias",
            PostReturnMethod = postReturn,
        };
        var contract = new ComponentBoundaryContract("", "", [], [function]);
        var semantic = CanonicalAbiNames.Export(
            function.InterfaceName, function.FunctionName, WasmTarget.Wasm32);
        var semanticPostReturn = CanonicalAbiNames.PostReturn(
            function.InterfaceName, function.FunctionName, WasmTarget.Wasm32);

        var merged = new ComponentExportMerger().Merge(
            [
                new ProgramExport(semantic, method),
                new ProgramExport(semanticPostReturn, postReturn),
            ],
            contract,
            WasmTarget.Wasm32);

        Assert.Equal(
            [
                "cm32p2|math-alias|add-value",
                "cm32p2|math-alias|add-value_post",
            ],
            merged.Select(export => export.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportMergerRejectsConflictsAtReplacedSemanticNames(bool postReturnConflict)
    {
        var method = ValidationTestData.Program().EntryPoint!.Key;
        var postReturn = new EntityKey(ValidationTestData.Assembly, 0x06000002);
        var conflicting = new EntityKey(ValidationTestData.Assembly, 0x06000003);
        var function = new CanonicalAbiFunction(
            "example:worker/math-api@1.0.0",
            "add-value",
            method,
            [],
            null)
        {
            CoreInterfaceName = "math-alias",
            PostReturnMethod = postReturn,
        };
        var semantic = CanonicalAbiNames.Export(
            function.InterfaceName, function.FunctionName, WasmTarget.Wasm32);
        var semanticPostReturn = CanonicalAbiNames.PostReturn(
            function.InterfaceName, function.FunctionName, WasmTarget.Wasm32);
        var exports = postReturnConflict
            ? ImmutableArray.Create(
                new ProgramExport(semantic, method),
                new ProgramExport(semanticPostReturn, conflicting))
            : ImmutableArray.Create(
                new ProgramExport(semantic, conflicting),
                new ProgramExport(semanticPostReturn, postReturn));

        var failure = Assert.Throws<CompilerException>(() =>
            new ComponentExportMerger().Merge(
                exports,
                new ComponentBoundaryContract("", "", [], [function]),
                WasmTarget.Wasm32));

        Assert.Equal(DiagnosticCode.ComponentContract, failure.Diagnostic.Code);
        Assert.Contains(postReturnConflict ? semanticPostReturn : semantic,
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExportMergerRejectsMissingContract()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentExportMerger().Merge([], null!, WasmTarget.Wasm32));
    }

    // The key a binding generated before world keys names its interface by.
    [Theory]
    [InlineData("example:app@1.0.0/api", true)]
    [InlineData("a:b@1/i", true)]
    [InlineData("a:b:c@1@2/i", true)]
    [InlineData("example:app/api", false)]
    [InlineData("api", false)]
    [InlineData("example:app@1.0.0/", false)]
    [InlineData("example:app@1.0.0/api/more", false)]
    [InlineData("example@1.0.0/api", false)]
    [InlineData(":app@1.0.0/api", false)]
    [InlineData("example:@1.0.0/api", false)]
    [InlineData("example:app@/api", false)]
    [InlineData("a@b:c@1/i", false)]
    [InlineData("abc@1/i", false)]
    public void RecognizesThePackageFirstInterfaceKey(string name, bool packageFirst) =>
        Assert.Equal(packageFirst, ComponentReachabilityValidator.IsPackageFirstKey(name));

    [Fact]
    public void ReachabilityValidatorAcceptsEmptyProgram()
    {
        var validator = new ComponentReachabilityValidator();
        var program = ValidationTestData.Program();
        var methods = new MethodRepository();
        var symbols = new SymbolFormatter();
        validator.Validate(
            program,
            [],
            ComponentBoundaryContract.Empty,
            methods,
            symbols);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            null!, [], ComponentBoundaryContract.Empty, methods, symbols));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            program, [], null!, methods, symbols));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            program, [], ComponentBoundaryContract.Empty, null!, symbols));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            program, [], ComponentBoundaryContract.Empty, methods, null!));
    }

    // A Component Model built-in is part of the canonical ABI, not of the world.
    [Fact]
    public void ReachabilityValidatorAcceptsBuiltInsOutsideTheContract()
    {
        var method = ValidationTestData.Program().EntryPoint! with
        {
            WitImport = new WitImportDeclaration("$root", "[waitable-set-new]") { IsCanonicalBuiltIn = true },
        };
        var program = ValidationTestData.Program() with
        {
            WitImportMethods = [method],
        };

        new ComponentReachabilityValidator().Validate(
            program,
            [],
            new ComponentBoundaryContract("", "", [Function(new EntityKey(ValidationTestData.Assembly, 0x06000009), "other")], []),
            new MethodRepository(method),
            new SymbolFormatter());
    }

    [Fact]
    public void ReachabilityValidatorRequiresAContractForReachableBindings()
    {
        var method = ValidationTestData.Program().EntryPoint! with
        {
            WitImport = new WitImportDeclaration("", "run"),
        };
        var program = ValidationTestData.Program() with
        {
            WitImportMethods = [method],
        };

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentReachabilityValidator().Validate(
                program,
                [],
                ComponentBoundaryContract.Empty,
                new MethodRepository(method),
                new SymbolFormatter()));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Equal("Unit::Run", exception.Diagnostic.Method);
    }

    [Fact]
    public void ReachabilityValidatorAcceptsDeclaredImportsAndExports()
    {
        var original = ValidationTestData.Program().EntryPoint!;
        var imported = original with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000002),
            WitImport = new WitImportDeclaration("", "import"),
        };
        var exported = original with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000003),
            WitExport = new WitExportDeclaration("", "export"),
        };
        var program = ValidationTestData.Program() with
        {
            WitImportMethods = [imported],
        };
        var contract = new ComponentBoundaryContract(
            "",
            "",
            [Function(imported.Key, "import")],
            [Function(exported.Key, "export")]);

        new ComponentReachabilityValidator().Validate(
            program,
            [new ProgramExport("cm32p2||export", exported.Key)],
            contract,
            new MethodRepository(imported, exported),
            new SymbolFormatter());
    }

    [Fact]
    public void ReachabilityValidatorAcceptsEquivalentManagedImportBindings()
    {
        var original = ValidationTestData.Program().EntryPoint!;
        var reachable = original with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000006),
            WitImport = new WitImportDeclaration("example:host/api", "read"),
        };
        var representative = original with
        {
            Key = new EntityKey(new AssemblyIdentity("GeneratedBindings"), 0x06000001),
            WitImport = new WitImportDeclaration("example:host/api", "read"),
        };
        var program = ValidationTestData.Program() with
        {
            WitImportMethods = [reachable],
        };
        var contract = new ComponentBoundaryContract(
            "",
            "",
            [new CanonicalAbiFunction(
                "example:host/api",
                "read",
                representative.Key,
                [],
                null)],
            []);
        var identity = reachable.WitImport.Identity;

        Assert.Equal("example:host/api", identity.InterfaceName);
        Assert.Equal("read", identity.FunctionName);
        Assert.Equal(identity, Assert.Single(contract.Imports).Identity);

        new ComponentReachabilityValidator().Validate(
            program,
            [],
            contract,
            new MethodRepository(reachable),
            new SymbolFormatter());
    }

    // Bindings generated before world keys name an interface package first; one
    // the world does not declare is reported with how to regenerate it, for an
    // import or an export.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReachabilityValidatorPointsPackageFirstBindingsAtRegeneration(bool import)
    {
        var original = ValidationTestData.Program().EntryPoint!;
        var binding = original with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000008),
            WitImport = import ? new WitImportDeclaration("example:host@1.0.0/api", "read") : null,
            WitExport = import ? null : new WitExportDeclaration("example:host@1.0.0/api", "read"),
        };
        var program = ValidationTestData.Program() with { WitImportMethods = import ? [binding] : [] };
        var contract = new ComponentBoundaryContract("", "", [Function(new(ValidationTestData.Assembly, 0x06000009), "other")], []);

        var exception = Assert.Throws<CompilerException>(() => new ComponentReachabilityValidator().Validate(
            program,
            import ? [] : [new ProgramExport("cm32p2|example:host@1.0.0/api|read", binding.Key)],
            contract,
            new MethodRepository(binding),
            new SymbolFormatter()));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Contains("'example:host@1.0.0/api' by the package-first key", exception.Diagnostic.Message);
        Assert.Contains("regenerate the bindings", exception.Diagnostic.Message);
    }

    [Fact]
    public void ReachabilityValidatorRequiresTheFullImportIdentity()
    {
        var method = ValidationTestData.Program().EntryPoint! with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000007),
            WitImport = new WitImportDeclaration("example:host/first", "read"),
        };
        var program = ValidationTestData.Program() with
        {
            WitImportMethods = [method],
        };

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentReachabilityValidator().Validate(
                program,
                [],
                new ComponentBoundaryContract(
                    "",
                    "",
                    [new CanonicalAbiFunction(
                        "example:host/second",
                        "read",
                        method.Key,
                        [],
                        null)],
                    []),
                new MethodRepository(method),
                new SymbolFormatter()));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Equal("reachable WIT binding imports 'example:host/first', which the selected world does not " +
            "import; add `import example:host/first;` to the world, or remove the code that reaches it",
            exception.Diagnostic.Message);
    }

    [Fact]
    public void ReachabilityValidatorReportsTheFirstMissingBinding()
    {
        var method = ValidationTestData.Program().EntryPoint! with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000004),
            WitImport = new WitImportDeclaration("", "missing"),
        };
        var program = ValidationTestData.Program() with
        {
            WitImportMethods = [method],
        };

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentReachabilityValidator().Validate(
                program,
                [],
                new ComponentBoundaryContract(
                    "",
                    "",
                    [Function(new EntityKey(ValidationTestData.Assembly, 0x06000010), "other")],
                    []),
                new MethodRepository(method),
                new SymbolFormatter()));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Equal("Unit::Run", exception.Diagnostic.Method);
        Assert.Equal("reachable WIT binding imports function 'missing', which the selected world does not import; " +
            "add it to the world's imports, or remove the code that reaches it", exception.Diagnostic.Message);
    }

    // The world is named as it is selected, namespace:package/world@version.
    [Theory]
    [InlineData("example:app@1.0.0", "service", "", "missing",
        "reachable WIT binding exports function 'missing', which world 'example:app/service@1.0.0' does not export; " +
        "add it to the world's exports, or remove the code that reaches it")]
    [InlineData("example:app", "service", "example:app/handler", "handle",
        "reachable WIT binding exports 'example:app/handler', which world 'example:app/service' does not export; " +
        "add `export example:app/handler;` to the world, or remove the code that reaches it")]
    public void ReachabilityValidatorNamesWhatTheWorldDoesNotExport(
        string package, string world, string interfaceName, string function, string message)
    {
        var method = ValidationTestData.Program().EntryPoint! with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000005),
            WitExport = new WitExportDeclaration(interfaceName, function),
        };

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentReachabilityValidator().Validate(
                ValidationTestData.Program(),
                [new ProgramExport("cm32p2||missing", method.Key)],
                new ComponentBoundaryContract(package, world, [],
                    [Function(ValidationTestData.Program().EntryPoint!.Key)]),
                new MethodRepository(method),
                new SymbolFormatter()));

        Assert.Equal(message, exception.Diagnostic.Message);
    }

    [Fact]
    public void ReachabilityValidatorReportsMissingExports()
    {
        var method = ValidationTestData.Program().EntryPoint! with
        {
            Key = new EntityKey(ValidationTestData.Assembly, 0x06000005),
            WitExport = new WitExportDeclaration("", "missing"),
        };
        var program = ValidationTestData.Program();

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentReachabilityValidator().Validate(
                program,
                [new ProgramExport("cm32p2||missing", method.Key)],
                new ComponentBoundaryContract(
                    "",
                    "",
                    [],
                    [Function(ValidationTestData.Program().EntryPoint!.Key)]),
                new MethodRepository(method),
                new SymbolFormatter()));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
    }

    private static CanonicalAbiFunction Function(EntityKey method, string name = "run") =>
        new("", name, method, [], null);

    private static object NullDependency(Type type) => type.IsInterface
        ? DispatchProxy.Create(type, typeof(NullCapabilityProxy))
        : type.IsValueType
            ? Activator.CreateInstance(type)!
            : RuntimeHelpers.GetUninitializedObject(type);

    public class NullCapabilityProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType.IsValueType == true
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }

    private sealed class MethodRepository(params MethodDefinitionModel[] methods) : IMethodRepository
    {
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods =
            methods.ToDictionary(method => method.Key);

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
    }

    private sealed class SymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) =>
            "Unit::" + method.Name;
    }
}
