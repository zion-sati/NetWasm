using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed partial class CompilerArchitectureTests
{
    [Fact]
    public void CompilerNamespacesMatchTheirDirectories()
    {
        var sourceRoot = Path.Combine(TestAssets.FindRepositoryRoot(), "src");
        var sourceFiles = Directory.EnumerateDirectories(
                sourceRoot,
                "NetWasm.Compiler*")
            .SelectMany(project => Directory.EnumerateFiles(
                project,
                "*.cs",
                SearchOption.AllDirectories).Select(path => (Project: project, Path: path)))
            .Where(item => !item.Path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) &&
                !string.Equals(
                    Path.GetFileName(item.Path),
                    "AssemblyInfo.cs",
                    StringComparison.Ordinal));

        foreach (var sourceFile in sourceFiles)
        {
            var directory = Path.GetRelativePath(
                sourceFile.Project,
                Path.GetDirectoryName(sourceFile.Path)!);
            var projectNamespace = Path.GetFileName(sourceFile.Project);
            var expected = directory == "."
                ? projectNamespace
                : projectNamespace + "." + directory.Replace(
                    Path.DirectorySeparatorChar,
                    '.');
            var declaration = File.ReadLines(sourceFile.Path)
                .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal));
            Assert.Equal($"namespace {expected};", declaration);
        }
    }

    [Fact]
    public void RootCompositionDelegatesRegistrationToFeatures()
    {
        var source = File.ReadAllText(Path.Combine(
            TestAssets.FindRepositoryRoot(),
            "src",
            "NetWasm.Compiler",
            "NetWasmCompiler.cs"));

        Assert.Equal(1, source.Split("AddSingleton<", StringSplitOptions.None).Length - 1);
        Assert.Contains("AddCompilerAnalysis()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerDiagnostics()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerValidation()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerExceptionTypes()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerLayout()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerLoading()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerEmission()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerGarbageCollection()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerInterop()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerEntryPoints()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerComponentModel()", source, StringComparison.Ordinal);
        Assert.Contains("AddCompilerPipeline()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Wasm64ExecutionDoesNotUseTheWasm32OnlyInlineHost()
    {
        var testRoot = Path.Combine(
            TestAssets.FindRepositoryRoot(),
            "tests",
            "NetWasm.Compiler.Tests");
        var violations = Directory.EnumerateFiles(
                testRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path))
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => UsesInlineHost(method) &&
                    (AcceptsWasmTarget(method) || SelectsWasm64(method)))
                .Select(method => $"{path}:{method.Identifier.ValueText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RegistrationFilesContainOnlyTheirFeatureExtensionType()
    {
        var sourceFiles = Directory.EnumerateFiles(
            Path.Combine(TestAssets.FindRepositoryRoot(), "src"),
            "*ServiceCollectionExtensions.cs",
            SearchOption.AllDirectories).Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

        foreach (var sourceFile in sourceFiles)
        {
            var declarations = File.ReadLines(sourceFile)
                .Select(line => line.Trim())
                .Where(line => line.Contains(" static class ", StringComparison.Ordinal))
                .ToArray();
            var expected = Path.GetFileNameWithoutExtension(sourceFile);

            Assert.Single(declarations);
            Assert.Contains($"class {expected}", declarations[0], StringComparison.Ordinal);

            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), cancellationToken: TestContext.Current.CancellationToken);
            var extensionType = syntax.GetRoot(TestContext.Current.CancellationToken)
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single();
            Assert.Contains(
                SyntaxKind.StaticKeyword,
                extensionType.Modifiers.Select(modifier => modifier.Kind()));

            var methods = extensionType.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Modifiers.Any(
                    modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
                .ToArray();
            Assert.NotEmpty(methods);
            Assert.All(methods, method =>
            {
                Assert.Contains(
                    SyntaxKind.PublicKeyword,
                    method.Modifiers.Select(modifier => modifier.Kind()));
                Assert.Contains(
                    SyntaxKind.StaticKeyword,
                    method.Modifiers.Select(modifier => modifier.Kind()));
                var firstParameter = method.ParameterList.Parameters.FirstOrDefault();
                Assert.NotNull(firstParameter);
                Assert.Contains(
                    SyntaxKind.ThisKeyword,
                    firstParameter!.Modifiers.Select(modifier => modifier.Kind()));
                Assert.Equal("IServiceCollection", firstParameter.Type?.ToString());
            });
        }
    }

    [Fact]
    public void CompilerStagesDoNotUseAServiceLocator()
    {
        var violations = new List<string>();
        foreach (var sourceFile in EnumerateCompilerSourceFiles())
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), cancellationToken: TestContext.Current.CancellationToken);
            var root = syntax.GetRoot(TestContext.Current.CancellationToken);
            if (root.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier =>
                    identifier.Identifier.Text is "IServiceProvider" or
                    "IServiceScopeFactory"))
            {
                violations.Add($"{sourceFile}: service-provider contract");
            }

            foreach (var invocation in root.DescendantNodes()
                         .OfType<InvocationExpressionSyntax>()
                         .Where(IsServiceLocatorInvocation))
            {
                var containingType = invocation.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault();
                if (containingType is null || !containingType.Modifiers.Any(
                        modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
                {
                    violations.Add($"{sourceFile}: {invocation}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuntimeArgumentFactoriesReturnOnlyAbstractStages()
    {
        Type[] factories =
        [
            typeof(IWholeProgramAnalyzerFactory),
            typeof(IRootMapAnalyzerFactory),
            typeof(IManagedWasmEmitterFactory),
        ];

        Assert.All(
            factories.SelectMany(type => type.GetMethods()),
            method => Assert.True(
                method.ReturnType.IsInterface,
                $"{method.DeclaringType?.Name}.{method.Name} returns concrete " +
                method.ReturnType.Name));
    }

    [Fact]
    public void BehavioralImplementationsConstructNoConcreteCollaborators()
    {
        var behavioralTypes = GetProductionTypes()
            .Where(IsBehavioralImplementation)
            .ToArray();
        var concreteCollaborators = behavioralTypes
            .SelectMany(type => type.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters()
                    .Where(parameter => ContainsConcreteBehavioralType(
                        parameter.ParameterType))
                    .Select(parameter => $"{type.FullName}({parameter.Name}: " +
                                         $"{parameter.ParameterType.FullName})")))
            .ToArray();

        var concreteConstruction = FindConcreteBehavioralConstruction();
        var compatibilityConstructors = behavioralTypes
            .Where(type => !IsFactory(type))
            .Where(type => type.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Length == 0))
            .Where(type => type.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance).Length > 1)
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        var violations = concreteCollaborators
            .Select(value => $"concrete constructor collaborator: {value}")
            .Concat(concreteConstruction.Select(value =>
                $"concrete collaborator construction: {value}"))
            .Concat(compatibilityConstructors.Select(value =>
                $"compatibility constructor: {value}"))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BehavioralImplementationsExposeOnePreciseDomainVerb()
    {
        var productionTypes = GetProductionTypes().ToArray();
        var genericNames = productionTypes
            .Where(IsBehavioralImplementation)
            .Where(type => IsVagueBehavioralName(type.Name))
            .Select(type => type.FullName)
            .ToArray();
        var multipleCapabilities = productionTypes
            .Select(type => new
            {
                Type = type,
                Capabilities = type.GetInterfaces()
                    .Where(IsCompilerBehavioralInterface)
                    .ToArray(),
            })
            .Where(item => item.Capabilities.Length > 1)
            .Select(item => $"{item.Type.FullName}: " + string.Join(
                ", ",
                item.Capabilities.Select(type => type.Name)))
            .ToArray();
        var impreciseCapabilities = productionTypes
            .SelectMany(type => type.GetInterfaces()
                .Where(IsCompilerBehavioralInterface))
            .Distinct()
            .Select(type => new
            {
                Type = type,
                Verbs = type.GetMethods()
                    .Where(method => !method.IsSpecialName)
                    .Where(method => method.Name != nameof(IDisposable.Dispose) ||
                                     !typeof(IDisposable).IsAssignableFrom(type))
                    .Select(method => method.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            })
            .Where(item => item.Verbs.Length != 1 ||
                           item.Verbs[0] is "Act" or "Acts")
            .Select(item => $"{item.Type.FullName}: " + string.Join(", ", item.Verbs))
            .ToArray();
        var implementationVerbMismatches = productionTypes
            .Select(type => new
            {
                Type = type,
                CapabilityVerbs = type.GetInterfaces()
                    .Where(IsCompilerBehavioralInterface)
                    .SelectMany(capability => capability.GetMethods())
                    .Where(method => !method.IsSpecialName)
                    .Select(method => method.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                PublicVerbs = type.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)
                    .Where(method => !method.IsSpecialName)
                    .Where(method => method.Name != nameof(IDisposable.Dispose) ||
                                     !typeof(IDisposable).IsAssignableFrom(type))
                    .Select(method => method.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            })
            .Where(item => item.CapabilityVerbs.Length != 0 &&
                           item.PublicVerbs.Except(
                               item.CapabilityVerbs,
                               StringComparer.Ordinal).Any())
            .Select(item => $"{item.Type.FullName}: " + string.Join(", ",
                item.PublicVerbs.Except(item.CapabilityVerbs, StringComparer.Ordinal)))
            .ToArray();
        var violations = genericNames.Select(name => $"generic name: {name}")
            .Concat(multipleCapabilities.Select(value => $"multiple capabilities: {value}"))
            .Concat(impreciseCapabilities.Select(value => $"imprecise capability: {value}"))
            .Concat(implementationVerbMismatches.Select(value =>
                $"public verbs outside capability: {value}"))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BehavioralImplementationsUsePreciseCapabilityNames()
    {
        string[] vagueSuffixes =
        [
            "Act",
            "Acts",
            "Actor",
            "Actors",
            "Coordinator",
            "Context",
            "Helper",
            "Manager",
            "Orchestrator",
            "Processor",
            "Utility",
        ];
        var violations = GetProductionTypes()
            .Where(type => type.GetInterfaces().Any(IsCompilerBehavioralInterface))
            .Where(type => vagueSuffixes.Any(suffix =>
                type.Name.Equals(suffix, StringComparison.Ordinal) ||
                type.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BehavioralImplementationsExposeACapabilityContract()
    {
        var violations = GetProductionTypes()
            .Where(type => type.GetProperty(
                "EqualityContract",
                BindingFlags.Instance | BindingFlags.NonPublic) is null)
            .Select(type => new
            {
                Type = type,
                PublicVerbs = type.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)
                    .Where(method => !method.IsSpecialName)
                    .Where(method => method.Name != nameof(IDisposable.Dispose))
                    .Where(method => method.GetBaseDefinition().DeclaringType != typeof(object))
                    .Select(method => method.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                Capabilities = type.GetInterfaces()
                    .Where(interfaceType => interfaceType != typeof(IDisposable))
                    .Where(interfaceType => interfaceType.GetMethods()
                        .Any(method => !method.IsSpecialName))
                    .ToArray(),
            })
            .Where(item => item.PublicVerbs.Length != 0 && item.Capabilities.Length == 0)
            .Select(item => $"{item.Type.FullName}: " +
                            string.Join(", ", item.PublicVerbs))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RecordsContainNoBehavioralCapabilities()
    {
        var capabilityRecords = GetProductionTypes()
            .Where(type => type.GetProperty(
                "EqualityContract",
                BindingFlags.Instance | BindingFlags.NonPublic) is not null)
            .Select(type => new
            {
                Type = type,
                Members = GetRecordCapabilityMembers(type),
            })
            .Select(item => new
            {
                item.Type,
                Capabilities = IsCapabilityRegistrationRecord(item.Type)
                    ? []
                    : item.Members,
            })
            .Where(item => item.Capabilities.Length != 0)
            .Select(item => $"{item.Type.FullName}: " +
                            string.Join(", ", item.Capabilities))
            .ToArray();

        Assert.True(
            capabilityRecords.Length == 0,
            string.Join(Environment.NewLine, capabilityRecords));
    }

    private static IEnumerable<Type> GetProductionTypes() =>
        typeof(ManagedWasmEmitter).Assembly.GetTypes()
            .Concat(typeof(NetWasmCompilationPipeline).Assembly.GetTypes())
            .Concat(typeof(RootMapAnalyzer).Assembly.GetTypes())
            .Concat(typeof(ManagedAssembly).Assembly.GetTypes())
            .Concat(typeof(ControlFlowGraph).Assembly.GetTypes())
            .Concat(typeof(WitTypeDeclarationWriter).Assembly.GetTypes())
            .Concat(typeof(CliTypeIdentity).Assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false });

    private static bool IsCompilerBehavioralInterface(Type type) =>
        type.IsInterface &&
        type.Namespace?.StartsWith("NetWasm.Compiler", StringComparison.Ordinal) == true &&
        type.GetMethods().Any(method => !method.IsSpecialName);

    private static bool UsesInlineHost(MethodDeclarationSyntax method) =>
        method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression switch
            {
                IdentifierNameSyntax identifier =>
                    identifier.Identifier.ValueText == "ExecuteWithNode",
                MemberAccessExpressionSyntax member =>
                    member.Name.Identifier.ValueText == "ExecuteWithNode",
                _ => false,
            });

    private static bool AcceptsWasmTarget(MethodDeclarationSyntax method) =>
        method.ParameterList.Parameters.Any(parameter =>
            parameter.Type?.ToString().EndsWith(
                nameof(WasmTarget),
                StringComparison.Ordinal) == true);

    private static bool SelectsWasm64(MethodDeclarationSyntax method) =>
        method.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(member => member.Expression.ToString().EndsWith(
                    nameof(WasmTarget),
                    StringComparison.Ordinal) &&
                member.Name.Identifier.ValueText == nameof(WasmTarget.Wasm64));

    private static bool IsBehavioralImplementation(Type type) =>
        type.IsClass && type.GetInterfaces().Any(IsCompilerBehavioralInterface);

    private static bool IsVagueBehavioralName(string name)
    {
        string[] vagueParts =
        [
            "Act",
            "Acts",
            "Actor",
            "Actors",
            "Context",
            "Coordinator",
            "Helper",
            "Manager",
            "Orchestrator",
            "Processor",
            "Utility",
        ];

        return vagueParts.Any(part => name.Equals(part, StringComparison.Ordinal) ||
                                      name.EndsWith(part, StringComparison.Ordinal));
    }

    private static bool IsFactory(Type type) => IsFactory(type.Name);

    private static bool IsFactory(string name) =>
        name.EndsWith("Factory", StringComparison.Ordinal);

    private static bool ContainsBehavioralCapability(Type type)
    {
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            return false;
        }

        if (type.IsByRef || type.IsPointer)
        {
            return type.GetElementType() is { } byRefElement &&
                   ContainsBehavioralCapability(byRefElement);
        }

        if (IsCompilerBehavioralInterface(type) ||
            typeof(Delegate).IsAssignableFrom(type))
        {
            return true;
        }

        return type.IsArray
            ? type.GetElementType() is { } arrayElement &&
              ContainsBehavioralCapability(arrayElement)
            : type.IsGenericType && type.GetGenericArguments()
                .Any(ContainsBehavioralCapability);
    }

    private static string[] GetRecordCapabilityMembers(Type type) =>
        type.GetProperties(BindingFlags.Instance |
                           BindingFlags.Public |
                           BindingFlags.NonPublic)
            .Where(property => ContainsBehavioralCapability(property.PropertyType))
            .Select(property => $"{property.Name}: {property.PropertyType}")
            .Concat(type.GetFields(BindingFlags.Instance |
                                   BindingFlags.Public |
                                   BindingFlags.NonPublic)
                .Where(field => ContainsBehavioralCapability(field.FieldType))
                .Select(field => $"{field.Name}: {field.FieldType}"))
            .ToArray();

    private static bool IsCapabilityRegistrationRecord(Type type)
    {
        var capabilityProperties = type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => ContainsBehavioralCapability(property.PropertyType))
            .ToArray();
        var hasDiscriminator = type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(property => property.PropertyType.IsEnum);

        return capabilityProperties.Length == 1 &&
               !ContainsDelegate(capabilityProperties[0].PropertyType) &&
               hasDiscriminator;
    }

    private static bool ContainsDelegate(Type type)
    {
        if (type.IsByRef || type.IsPointer)
        {
            return type.GetElementType() is { } byRefElement &&
                   ContainsDelegate(byRefElement);
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            return true;
        }

        return type.IsArray
            ? type.GetElementType() is { } arrayElement && ContainsDelegate(arrayElement)
            : type.IsGenericType && type.GetGenericArguments().Any(ContainsDelegate);
    }

    private static bool ContainsConcreteBehavioralType(Type type)
    {
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            return false;
        }

        if (type.IsByRef || type.IsPointer)
        {
            return type.GetElementType() is { } byRefElement &&
                   ContainsConcreteBehavioralType(byRefElement);
        }

        if (type.IsClass && IsBehavioralImplementation(type))
        {
            return true;
        }

        return type.IsArray
            ? type.GetElementType() is { } arrayElement &&
              ContainsConcreteBehavioralType(arrayElement)
            : type.IsGenericType && type.GetGenericArguments()
                .Any(ContainsConcreteBehavioralType);
    }

    private static IEnumerable<string> FindConcreteBehavioralConstruction()
    {
        var behavioralNames = GetProductionTypes()
            .Where(IsBehavioralImplementation)
            .Where(type => !IsFactory(type))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        var sourceFiles = EnumerateCompilerSourceFiles();

        foreach (var sourceFile in sourceFiles)
        {
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile));
            var root = syntax.GetRoot();
            foreach (var creation in root.DescendantNodes()
                         .OfType<ObjectCreationExpressionSyntax>())
            {
                var containingType = creation.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault();
                if (containingType is null ||
                    !behavioralNames.Contains(containingType.Identifier.Text) ||
                    IsFactory(containingType.Identifier.Text))
                {
                    continue;
                }

                var createdName = creation.Type.ToString();
                if (createdName.StartsWith("global::", StringComparison.Ordinal))
                {
                    createdName = createdName["global::".Length..];
                }
                createdName = createdName
                    .Split('.', StringSplitOptions.RemoveEmptyEntries)
                    .Last()
                    .Split('<', StringSplitOptions.RemoveEmptyEntries)
                    .First();
                if (!behavioralNames.Contains(createdName))
                {
                    continue;
                }

                var line = creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                yield return $"{sourceFile}:{line}: {containingType.Identifier.Text} -> " +
                             createdName;
            }
        }
    }

    private static IEnumerable<string> EnumerateCompilerSourceFiles()
    {
        var sourceRoot = Path.Combine(TestAssets.FindRepositoryRoot(), "src");
        return Directory.EnumerateDirectories(sourceRoot, "NetWasm.Compiler*")
            .SelectMany(project => Directory.EnumerateFiles(
                project,
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
    }

    private static bool IsServiceLocatorInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        return memberAccess.Name.Identifier.Text is "GetService" or
            "GetRequiredService" or
            "GetServices" or
            "GetKeyedService" or
            "GetRequiredKeyedService" or
            "BuildServiceProvider" or
            "CreateScope" or
            "CreateAsyncScope";
    }

}
