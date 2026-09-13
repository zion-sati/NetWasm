using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawWitImportCatalogBuilderTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CatalogRetainsDeclarationsAndSeparatesAllResourceImportKinds(WasmTarget target)
    {
        var (document, world) = Fixture();
        var dependencies = new RecordingDependencies();

        var catalog = Create(dependencies).Build(document, world, target);

        Assert.Same(document, catalog.Document);
        Assert.Same(world, catalog.World);
        Assert.Equal(target, catalog.Target);
        Assert.Equal(6, catalog.Imports.Count);
        Assert.Equal((document, world), dependencies.Validation);
        Assert.Equal("validate", dependencies.Calls[0]);
        Assert.Equal(6, dependencies.Requests.Count);
        Assert.All(dependencies.Requests, request => Assert.Equal(target, request.Target));
        var functions = catalog.Imports.Values.OfType<RawWitImportDeclaration.Callable>().ToArray();
        Assert.Collection(functions.OrderBy(function => function.InterfaceName, StringComparer.Ordinal),
            function =>
            {
                Assert.Equal("", function.InterfaceName);
                Assert.Equal("example:files/main@1.0.0", function.DeploymentInterfaceName);
                Assert.Same(world.Imports[0].Function, function.Definition);
            },
            function =>
            {
                Assert.Equal("example:files@1.0.0/api", function.InterfaceName);
                Assert.Equal("example:files/api@1.0.0", function.DeploymentInterfaceName);
                Assert.Same(document.Interfaces[0].Functions[0], function.Definition);
            });
        var resources = catalog.Imports.Values.OfType<RawWitImportDeclaration.Resource>().ToArray();
        Assert.Equal([
            CanonicalAbiFunctionKind.ImportedResourceDrop, CanonicalAbiFunctionKind.ExportedResourceNew,
            CanonicalAbiFunctionKind.ExportedResourceRep, CanonicalAbiFunctionKind.ExportedResourceDrop,
        ], resources.Select(resource => resource.Kind).Order());
        Assert.All(resources, resource =>
        {
            Assert.Same(document.Types[0], resource.Definition);
            Assert.Equal("example:files@1.0.0/api", resource.InterfaceName);
            Assert.Equal("example:files/api@1.0.0", resource.DeploymentInterfaceName);
        });
        Assert.DoesNotContain(dependencies.Requests, request => request.Function.Kind == CanonicalAbiFunctionKind.ExportedResourceDestructor);
        Assert.DoesNotContain(functions, function => function.Definition.Name == "export-only");
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2")]
    public void RealNamingCompositionMatchesCompilerRootFunctionAndResourceIdentities(WasmTarget target, string prefix)
    {
        var (document, world) = Fixture();
        var builder = Assert.IsAssignableFrom<IRawWitImportCatalogBuilder>(new RawWitImportCatalogBuilder(
            new WitWorldValidator(), new RawCanonicalImportIdentityFormatter(),
            new WitWorldSpecifierFormatter(), new WitInterfaceSpecifierFormatter()));

        var catalog = builder.Build(document, world, target);

        Assert.Equal(6, catalog.Imports.Count);
        Assert.Same(world.Imports[0].Function,
            Assert.IsType<RawWitImportDeclaration.Callable>(catalog.Imports[new(prefix, "root")]).Definition);
        Assert.IsType<RawWitImportDeclaration.Callable>(catalog.Imports[new(prefix + "|example:files/api@1", "[method]file.read")]);
        Assert.Equal(CanonicalAbiFunctionKind.ImportedResourceDrop,
            Assert.IsType<RawWitImportDeclaration.Resource>(catalog.Imports[new(prefix + "|example:files/api@1", "file_drop")]).Kind);
        foreach (var suffix in new[] { "new", "rep", "drop" })
            Assert.IsType<RawWitImportDeclaration.Resource>(catalog.Imports[new(prefix + "|_ex_example:files/api@1", "file_" + suffix)]);
    }

    [Fact]
    public void EmptyWorldHasNoInventedImports()
    {
        var dependencies = new RecordingDependencies();
        var world = new WitWorld(0, "empty", "example:empty@1.0.0", [], []);

        var catalog = Create(dependencies).Build(new([], [], [world], [], "{}"), world, WasmTarget.Wasm32);

        Assert.Empty(catalog.Imports);
        Assert.Equal(["validate"], dependencies.Calls);
    }

    [Fact]
    public void CompatibleVersionCollisionsCannotSelectAnArbitraryDeclaration()
    {
        var (document, world) = Fixture();
        var second = document.Interfaces[0] with { Id = 1, Package = "example:files@1.9.0" };
        document = document with { Interfaces = document.Interfaces.Add(second) };
        world = world with { Imports = world.Imports.Add(new("second", 1, null)), Exports = [] };
        var builder = Assert.IsAssignableFrom<IRawWitImportCatalogBuilder>(new RawWitImportCatalogBuilder(
            new WitWorldValidator(), new RawCanonicalImportIdentityFormatter(),
            new WitWorldSpecifierFormatter(), new WitInterfaceSpecifierFormatter()));

        var failure = Assert.Throws<CompilerException>(() => builder.Build(document, world, WasmTarget.Wasm64));

        Assert.Equal(DiagnosticCode.ComponentContract, failure.Diagnostic.Code);
        Assert.Contains("colliding", failure.Diagnostic.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DependencyFailurePreventsLaterCatalogEntries(int failingCall)
    {
        var (document, world) = Fixture();
        var dependencies = new RecordingDependencies { FailingCall = failingCall };

        Assert.Same(dependencies.Failure, Assert.Throws<InvalidOperationException>(() => Create(dependencies).Build(document, world, WasmTarget.Wasm32)));
        Assert.Equal(failingCall, dependencies.Calls.Count);
    }

    [Fact]
    public void NamelessResourceIsRejectedBeforeItsIdentityIsFormatted()
    {
        var (document, world) = Fixture();
        document = document with { Types = document.Types.SetItem(0, document.Types[0] with { Name = null }) };
        var dependencies = new RecordingDependencies();

        Assert.Throws<ArgumentNullException>(() => Create(dependencies).Build(document, world, WasmTarget.Wasm32));
        Assert.Equal(2, dependencies.Requests.Count);
        Assert.All(dependencies.Requests, request => Assert.Equal(CanonicalAbiFunctionKind.Function, request.Function.Kind));
    }

    [Fact]
    public void AmbiguousWorldItemsAndMissingCollectionsFailBeforeWorldValidation()
    {
        var (document, world) = Fixture();
        var dependencies = new RecordingDependencies();
        var builder = Create(dependencies);

        Assert.Throws<CompilerException>(() => builder.Build(document, world with { Imports = default }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => builder.Build(document, world with { Exports = default }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => builder.Build(document with { Interfaces = default }, world, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => builder.Build(document with { Types = default }, world, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => builder.Build(document, world with { Imports = [null!] }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => builder.Build(document, world with { Imports = [new("missing", null, null)] }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => builder.Build(document, world with { Exports = [new("both", 0, world.Imports[0].Function)] }, WasmTarget.Wasm32));
        Assert.Empty(dependencies.Calls);
    }

    [Fact]
    public void MissingInputsAndInvalidTargetsFailBeforeDelegation()
    {
        var (document, world) = Fixture();
        var dependencies = new RecordingDependencies();
        var builder = Create(dependencies);

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, world, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => builder.Build(document, null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(document, world, (WasmTarget)99));
        var worlds = new WitWorldSpecifierFormatter();
        var interfaces = new WitInterfaceSpecifierFormatter();
        Assert.Throws<ArgumentNullException>(() => new RawWitImportCatalogBuilder(null!, dependencies, worlds, interfaces));
        Assert.Throws<ArgumentNullException>(() => new RawWitImportCatalogBuilder(dependencies, null!, worlds, interfaces));
        Assert.Throws<ArgumentNullException>(() => new RawWitImportCatalogBuilder(dependencies, dependencies, null!, interfaces));
        Assert.Throws<ArgumentNullException>(() => new RawWitImportCatalogBuilder(dependencies, dependencies, worlds, null!));
        Assert.Empty(dependencies.Calls);
    }

    private static IRawWitImportCatalogBuilder Create(RecordingDependencies dependencies) =>
        Assert.IsAssignableFrom<IRawWitImportCatalogBuilder>(new RawWitImportCatalogBuilder(
            dependencies, dependencies, new WitWorldSpecifierFormatter(),
            new WitInterfaceSpecifierFormatter()));

    private static (WitDocument Document, WitWorld World) Fixture()
    {
        var root = new WitFunction("root", [], null, new("freestanding"));
        var member = new WitFunction("[method]file.read", [], new WitTypeReference.Primitive("string"), new("method", 0));
        var definition = new WitInterface(0, "api", "example:files@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("file", 0).Add("alias", 1).Add("other", 2), [member]);
        var world = new WitWorld(0, "main", "example:files@1.0.0",
            [new("root", null, root), new("api", 0, null)],
            [new("api", 0, null), new("export-only", null, root with { Name = "export-only" })]);
        return (new([], [definition], [world],
            [Type(0, "file", "\"resource\""), Type(1, "alias", """{"type":0}"""), Type(2, "other", "\"unused\"")], "{}"), world);
    }

    private static WitTypeDefinition Type(int id, string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), 0);
    }

    private sealed class RecordingDependencies : IWitWorldValidator, IRawCanonicalImportIdentityFormatter
    {
        public List<string> Calls { get; } = [];
        public List<(CanonicalAbiFunction Function, WasmTarget Target)> Requests { get; } = [];
        public (WitDocument, WitWorld)? Validation { get; private set; }
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();

        public void Validate(WitDocument document, WitWorld world)
        {
            Enter("validate");
            Validation = (document, world);
        }

        public RawCanonicalImportIdentity Format(CanonicalAbiFunction abiFunction, WasmTarget target)
        {
            Enter("identity");
            Requests.Add((abiFunction, target));
            return new("module:" + abiFunction.InterfaceName, abiFunction.Kind + ":" + abiFunction.FunctionName);
        }

        private void Enter(string name)
        {
            Calls.Add(name);
            if (Calls.Count == FailingCall) throw Failure;
        }
    }
}
