using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetWasm.Compiler.Tests;

public sealed partial class CompilerArchitectureTests
{
    private static readonly string[] DownstreamControlFlowProjects =
    [
        "NetWasm.Compiler/",
        "NetWasm.Compiler.GarbageCollection/",
        "NetWasm.Compiler.Wasm/",
    ];

    private static readonly string[] RemovedControlFlowTypes =
    [
        "ControlFlowStructurizer",
        "IControlFlowStructurizer",
        "IControlFlowStructurizerFactory",
        "StructuredMethodBodyAdapter",
        "StructuredMethodLegacyAdapter",
    ];

    [Fact]
    public void DownstreamStagesConsumeNoDraftControlFlowTypes()
    {
        Assert.Empty(FindDraftControlFlowReferences(ReadCompilerSyntaxTrees()));
    }

    [Fact]
    public void DraftBoundaryIgnoresPrivateImplementationNamesButRejectsAccessibleDraftTypes()
    {
        var draft = (Path: "/src/NetWasm.Compiler.ControlFlow/Draft/Input.cs",
            Root: (CompilationUnitSyntax)CSharpSyntaxTree.ParseText("""
                namespace NetWasm.Compiler.ControlFlow.Draft;
                internal class DraftRegion { }
                internal class DraftProjector
                {
                    private class Projection { }
                }
                """, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken));
        var downstream = (Path: "/src/NetWasm.Compiler.Wasm/Input.cs",
            Root: (CompilationUnitSyntax)CSharpSyntaxTree.ParseText("""
                namespace NetWasm.Compiler.Wasm;
                internal class Projection { }
                internal class Consumer { private Projection _value; }
                """, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken));

        Assert.Empty(FindDraftControlFlowReferences([draft, downstream]));
        downstream.Root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText("""
            namespace NetWasm.Compiler.Wasm;
            internal class Consumer { private DraftRegion _value; }
            """, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        Assert.Equal(["DraftRegion"], FindDraftControlFlowReferences([draft, downstream]));
    }

    private static string[] FindDraftControlFlowReferences(
        (string Path, CompilationUnitSyntax Root)[] sources)
    {
        var draftTypes = sources
            .Where(source => Normalize(source.Path).Contains("NetWasm.Compiler.ControlFlow/Draft/", StringComparison.Ordinal))
            .SelectMany(source => source.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            // Private nested implementation types cannot be consumed by another
            // stage. Their unqualified names may legitimately occur elsewhere.
            .Where(declaration => !declaration.Modifiers.Any(SyntaxKind.PrivateKeyword))
            .Select(declaration => declaration.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        return sources
            .Where(source => IsDownstreamControlFlowProject(source.Path))
            .SelectMany(source => source.Root.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Select(identifier => identifier.Identifier.ValueText)
            .Where(draftTypes.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void ManagedFrontendPublishesNoStructuredOrWasmRepresentation()
    {
        var forbiddenTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "StructuredMethod",
            "WasmInstruction",
            "WasmModule",
            "WasmTarget",
        };
        var references = ReadCompilerSyntaxTrees()
            .Where(source => IsManagedFrontendSource(source.Path))
            .SelectMany(source => source.Root.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Select(identifier => identifier.Identifier.ValueText)
            .Where(forbiddenTypes.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(references);
    }

    [Fact]
    public void OnlyTheWasmMethodLowererConsumesTheStructuredBuilderFactory()
    {
        var consumers = ReadCompilerSyntaxTrees()
            .Where(source => !Normalize(source.Path).Contains(
                "/src/NetWasm.Compiler.ControlFlow/",
                StringComparison.Ordinal))
            .Where(source => source.Root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(identifier => identifier.Identifier.ValueText is
                    "IValidatedStructuredMethodBuilderFactory" or
                    "ValidatedStructuredMethodBuilderFactory"))
            .Select(source => Path.GetFileName(source.Path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["WasmMethodLowerer.cs"], consumers);
    }

    [Fact]
    public void ValidatedStructuredMethodHasOneProductionConstructionPath()
    {
        var sources = ReadCompilerSyntaxTrees();
        var constructions = sources
            .SelectMany(source => source.Root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>())
            .Where(creation => GetTerminalTypeName(creation.Type) == "StructuredMethod")
            .ToArray();

        // Every structured method is built and validated by the factory. The one other
        // construction is an outline's stand-in, which has no blocks, is marked as an
        // outline and is structured (through the factory) before it is emitted.
        Assert.Equal(
            ["StructuredControlFlowModel.cs", "StructuredMethodFactory.cs"],
            constructions.Select(construction => Path.GetFileName(construction.SyntaxTree.FilePath))
                .Order(StringComparer.Ordinal)
                .ToArray());

        var factoryCallers = sources
            .SelectMany(source => source.Root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Create",
                Expression: IdentifierNameSyntax
                {
                    Identifier.ValueText: "_structuredMethodFactory",
                },
            })
            .Select(invocation => Path.GetFileName(invocation.SyntaxTree.FilePath))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["StructuredMethodCompleter.cs"], factoryCallers);

        var removedReferences = sources
            .SelectMany(source => source.Root.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Select(identifier => identifier.Identifier.ValueText)
            .Where(RemovedControlFlowTypes.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(removedReferences);
    }

    [Fact]
    public void WasmEmissionReferencesEveryStructuredRegionAndExitVariant()
    {
        var sources = ReadCompilerSyntaxTrees();
        var structuredSources = sources
            .Where(source => Normalize(source.Path).Contains("NetWasm.Compiler.ControlFlow/Structured/", StringComparison.Ordinal))
            .ToArray();
        var wasmSources = sources
            .Where(source => Normalize(source.Path).Contains("NetWasm.Compiler.Wasm/", StringComparison.Ordinal))
            .ToArray();

        AssertVariantsReferenced(
            structuredSources,
            wasmSources.Where(source => Path.GetFileName(source.Path) == "StructuredControlFlowEmitter.cs"),
            "StructuredRegion");
        AssertVariantsReferenced(
            structuredSources,
            wasmSources.Where(source => Path.GetFileName(source.Path) == "ManagedMethodSequenceEmitter.cs"),
            "StructuredBlockExit");
    }

    private static void AssertVariantsReferenced(
        IEnumerable<(string Path, CompilationUnitSyntax Root)> declarations,
        IEnumerable<(string Path, CompilationUnitSyntax Root)> consumers,
        string baseTypeName)
    {
        var variants = declarations
            .SelectMany(source => source.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Where(declaration => declaration.BaseList?.Types.Any(
                baseType => GetTerminalTypeName(baseType.Type) == baseTypeName) == true)
            .Select(declaration => declaration.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        var referencedVariants = consumers
            .SelectMany(source => source.Root.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Select(identifier => identifier.Identifier.ValueText)
            .Where(variants.Contains)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(variants);
        Assert.True(variants.SetEquals(referencedVariants));
    }

    private static (string Path, CompilationUnitSyntax Root)[] ReadCompilerSyntaxTrees() =>
        EnumerateCompilerSourceFiles()
            .Select(sourcePath =>
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath), path: sourcePath);
                return (sourcePath, (CompilationUnitSyntax)tree.GetRoot());
            })
            .ToArray();

    private static bool IsDownstreamControlFlowProject(string sourcePath)
    {
        var normalizedPath = Normalize(sourcePath);
        return DownstreamControlFlowProjects.Any(
            project => normalizedPath.Contains($"/src/{project}", StringComparison.Ordinal));
    }

    private static bool IsManagedFrontendSource(string sourcePath)
    {
        var normalizedPath = Normalize(sourcePath);
        return normalizedPath.Contains(
                   "/src/NetWasm.Compiler/Analysis/",
                   StringComparison.Ordinal) ||
               normalizedPath.EndsWith(
                   "/src/NetWasm.Compiler/ReachableProgram.cs",
                   StringComparison.Ordinal) ||
               normalizedPath.Contains(
                   "/src/NetWasm.Compiler.GarbageCollection/",
                   StringComparison.Ordinal);
    }

    private static string GetTerminalTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        QualifiedNameSyntax qualified => GetTerminalTypeName(qualified.Right),
        AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText,
        _ => type.ToString(),
    };

    private static string Normalize(string path) => path.Replace('\\', '/');
}
