using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Catalogs;

public sealed class WitWorldFunctionProjectorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RequiresEveryCollaborator(int missing)
    {
        var worlds = new NoOpWorldValidator();
        var worldSpecifiers = new WitWorldSpecifierFormatter();
        var interfaceSpecifiers = new WitInterfaceSpecifierFormatter();
        var types = new WitTypeIdentityFormatter(interfaceSpecifiers);
        var javaScriptNames = new WitJavaScriptNameFormatter();

        Assert.Throws<ArgumentNullException>(() => new WitWorldFunctionProjector(
            missing == 0 ? null! : worlds,
            missing == 1 ? null! : worldSpecifiers,
            missing == 2 ? null! : interfaceSpecifiers,
            missing == 3 ? null! : types,
            missing == 4 ? null! : javaScriptNames,
            missing == 5 ? null! : WorkerTypes(interfaceSpecifiers)));
    }

    [Fact]
    public void ProjectsExactInterfaceAndWorldFunctionsInStableOrder()
    {
        var api = new WitInterface(
            0,
            "api",
            "example:contracts@1.2.3",
            [],
            [
                Function("zeta", [new("value", new WitTypeReference.Primitive("string"))], null),
                Function("alpha", [], new WitTypeReference.Primitive("u32")),
            ]);
        var world = new WitWorld(
            0,
            "command",
            "example:app@4.5.6",
            [
                new("api", 0, null),
                new("configure", null, Function(
                    "ignored-item-name",
                    [new("enabled", new WitTypeReference.Primitive("bool"))],
                    new WitTypeReference.Primitive("string"))),
            ],
            [new("run", null, Function("ignored-item-name", [], null))]);
        var document = new WitDocument([], [api], [world], [], "{}");

        var projection = Create().Project(document, world);

        Assert.Collection(
            projection.Imports,
            function =>
            {
                Assert.Equal("example:app/command@4.5.6", function.Interface);
                Assert.Equal("configure", function.Name);
                Assert.Equal(["bool"], function.Parameters);
                Assert.Equal(["string"], function.Results);
            },
            function =>
            {
                Assert.Equal("example:contracts/api@1.2.3", function.Interface);
                Assert.Equal("alpha", function.Name);
                Assert.Empty(function.Parameters);
                Assert.Equal(["u32"], function.Results);
            },
            function =>
            {
                Assert.Equal("example:contracts/api@1.2.3", function.Interface);
                Assert.Equal("zeta", function.Name);
                Assert.Equal(["string"], function.Parameters);
                Assert.Empty(function.Results);
            });
        var export = Assert.Single(projection.Exports);
        Assert.Equal("example:app/command@4.5.6", export.Interface);
        Assert.Equal("run", export.Name);
        Assert.Empty(export.Parameters);
        Assert.Empty(export.Results);
        Assert.Equal(
            ["example:app/command@4.5.6", "example:contracts/api@1.2.3"],
            projection.ImportModules);
        var workerExport = Assert.Single(projection.WorkerContract.Exports);
        Assert.Equal("run", workerExport.Operation);
        Assert.Equal("root", workerExport.Placement);
        Assert.Equal("run", workerExport.WorldItem);
        Assert.Null(workerExport.Interface);
        Assert.Equal(string.Empty, workerExport.JavaScriptRoot);
        Assert.Equal("run", workerExport.JavaScriptMember);
        Assert.Equal("ignored-item-name", workerExport.Function);
        Assert.Empty(workerExport.Parameters);
        Assert.Null(workerExport.Result);
        Assert.Equal("freestanding", workerExport.Kind.Name);
        Assert.Equal(2, projection.WorkerContract.SchemaVersion);
        Assert.Equal("example:app/command@4.5.6", projection.WorkerContract.World);
        Assert.Empty(projection.WorkerContract.Types);
        Assert.Null(projection.WorkerContract.Reactor);
    }

    [Fact]
    public void PreservesApplicationWorldStructureAndExactJavaScriptNames()
    {
        using var kind = System.Text.Json.JsonDocument.Parse("{\"record\":{\"fields\":[]}}");
        var api = new WitInterface(
            0,
            "math-api",
            "example:worker@1.0.0",
            [],
            [Function(
                "add-value",
                [new("left-value", new WitTypeReference.Primitive("s32"))],
                new WitTypeReference.Defined(0))]);
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [
                new("math-alias", 0, null),
                new("root-value", null, Function(
                    "ignored",
                    [new("right-value", new WitTypeReference.Primitive("u64"))],
                    new WitTypeReference.Primitive("s64"))),
            ]);
        var worker = new WitWorld(
            1,
            "worker",
            "example:worker@1.0.0",
            [],
            [
                new("math-alias", 0, null),
                new("root-value", null, Function(
                    "ignored",
                    [new("right-value", new WitTypeReference.Primitive("u64"))],
                    new WitTypeReference.Primitive("s64"))),
                new("framework", null, Function("framework", [], null)),
            ]);
        var document = new WitDocument(
            [],
            [api],
            [application, worker],
            [new(0, "point", kind.RootElement.Clone(), 0)],
            "{}");

        var contract = Create().Project(document, worker, application).WorkerContract;

        Assert.Equal("example:worker/application@1.0.0", contract.World);
        Assert.Collection(
            contract.Exports,
            export =>
            {
                Assert.Equal("example:worker/math-api@1.0.0/add-value", export.Operation);
                Assert.Equal("interface", export.Placement);
                Assert.Equal("math-alias", export.WorldItem);
                Assert.Equal("example:worker/math-api@1.0.0", export.Interface);
                Assert.Equal("mathAlias", export.JavaScriptRoot);
                Assert.Equal("addValue", export.JavaScriptMember);
                Assert.Equal("add-value", export.Function);
                var parameter = Assert.Single(export.Parameters);
                Assert.Equal("left-value", parameter.Name);
                Assert.Equal(new("primitive", "s32", null), parameter.Type);
                Assert.Equal(new("defined", null, 0), export.Result);
            },
            export =>
            {
                Assert.Equal("root-value", export.Operation);
                Assert.Equal("rootValue", export.JavaScriptMember);
                var parameter = Assert.Single(export.Parameters);
                Assert.Equal("right-value", parameter.Name);
                Assert.Equal(new("primitive", "u64", null), parameter.Type);
                Assert.Equal(new("primitive", "s64", null), export.Result);
            });
        var type = Assert.Single(contract.Types);
        Assert.Equal(0, type.Id);
        Assert.Equal("point", type.Name);
        Assert.True(type.Kind.TryGetProperty("record", out _));
        Assert.Equal(new("interface", "example:worker/math-api@1.0.0"), type.Owner);
        Assert.Null(contract.Reactor);
    }

    [Fact]
    public void ProjectsOptionalReactorFromPackagedWorkerWorld()
    {
        var reactorHost = new WitInterface(
            0,
            "reactor-host",
            "netwasm:runtime@1.0.0",
            [],
            [
                Function("watch", [], null),
                Function("cancel", [], null),
            ]);
        var reactorGuest = new WitInterface(
            1,
            "reactor-guest",
            "netwasm:runtime@1.0.0",
            [],
            [Function(
                "wake",
                [new("token", new WitTypeReference.Primitive("u32"))],
                null)]);
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new("value", null, Function("value", [], null))]);
        var worker = new WitWorld(
            1,
            "worker",
            "example:worker@1.0.0",
            [new("interface-0", 0, null)],
            [
                new("value", null, Function("value", [], null)),
                new("interface-1", 1, null),
            ]);
        var document = new WitDocument(
            [], [reactorHost, reactorGuest], [application, worker], [], "{}");

        var projected = Create().Project(document, worker, application);

        var reactor = Assert.IsType<WitWorkerReactor>(
            projected.WorkerContract.Reactor);
        Assert.Equal("netwasm:runtime/reactor-guest@1.0.0", reactor.Interface);
        Assert.Equal("interface1", reactor.JavaScriptRoot);
        Assert.Equal("wake", reactor.JavaScriptMember);
    }

    [Fact]
    public void ProjectsGuestOnlyReactorFromSynchronousPackagedWorkerWorld()
    {
        var reactorGuest = new WitInterface(
            0,
            "reactor-guest",
            "netwasm:runtime@1.0.0",
            [],
            [Function(
                "wake",
                [new("token", new WitTypeReference.Primitive("u32"))],
                null)]);
        var world = new WitWorld(
            0,
            "worker",
            "example:worker@1.0.0",
            [],
            [
                new("value", null, Function("value", [], null)),
                new("interface-0", 0, null),
            ]);
        var document = new WitDocument([], [reactorGuest], [world], [], "{}");

        var projected = Create().Project(document, world);

        Assert.Equal(
            new WitWorkerReactor(
                "netwasm:runtime/reactor-guest@1.0.0",
                "interface0",
                "wake"),
            projected.WorkerContract.Reactor);
    }

    [Fact]
    public void ProjectsApplicationContractAcrossSourceAndPackagedDocuments()
    {
        var sourceApi = new WitInterface(
            0,
            "math-api",
            "example:worker@1.0.0",
            [],
            [Function(
                "add",
                [
                    new("left", new WitTypeReference.Primitive("s32")),
                    new("right", new WitTypeReference.Primitive("s32")),
                ],
                new WitTypeReference.Primitive("s32"))]);
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [
                new("interface-0", 0, null),
                new("root-value", null, Function(
                    "root-value",
                    [],
                    new WitTypeReference.Primitive("s32"))),
            ]);
        var sourceWorker = application with { Id = 1, Name = "worker" };
        var source = new WitDocument(
            [],
            [sourceApi],
            [application, sourceWorker],
            [],
            "{}");

        var packagedApi = sourceApi with { Id = 1 };
        var packaged = new WitWorld(
            0,
            "worker",
            "example:packaged@1.0.0",
            [],
            [
                new("interface-1", 1, null),
                new("root-value", null, Function(
                    "root-value",
                    [],
                    new WitTypeReference.Primitive("s32"))),
            ]);
        var packagedDocument = new WitDocument(
            [],
            [null!, packagedApi],
            [packaged],
            [],
            "{}");

        var projected = Create().Project(
            packagedDocument,
            packaged,
            source,
            application,
            sourceWorker);

        Assert.Equal("example:worker/application@1.0.0", projected.WorkerContract.World);
        var export = Assert.Single(
            projected.WorkerContract.Exports,
            export => export.Operation == "example:worker/math-api@1.0.0/add");
        Assert.Equal("example:worker/math-api@1.0.0/add", export.Operation);
        Assert.Equal("interface0", export.JavaScriptRoot);
        Assert.Collection(
            projected.Exports,
            export => Assert.Equal(
                "example:worker/application@1.0.0",
                export.Interface),
            export => Assert.Equal(
                "example:worker/math-api@1.0.0",
                export.Interface));
    }

    [Fact]
    public void MatchesResourceFunctionKindsAcrossDocumentLocalTypeIds()
    {
        var sourceApi = new WitInterface(
            0,
            "counters",
            "example:worker@1.0.0",
            [],
            [new(
                "[constructor]counter",
                [new("initial", new WitTypeReference.Primitive("s32"))],
                new WitTypeReference.Defined(1),
                new("constructor", 0))]);
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new("counter-alias", 0, null)]);
        var sourceWorker = application with { Id = 1, Name = "worker" };
        var source = new WitDocument(
            [],
            [sourceApi],
            [application, sourceWorker],
            [
                Type(0, "counter", "\"resource\"", 0),
                Type(1, null, "{\"handle\":{\"own\":0}}"),
            ],
            "{}");

        var packagedApi = new WitInterface(
            1,
            "counters",
            "example:worker@1.0.0",
            [],
            [new(
                "[constructor]counter",
                [new("initial", new WitTypeReference.Primitive("s32"))],
                new WitTypeReference.Defined(2),
                new("constructor", 1))]);
        var packagedWorld = new WitWorld(
            0,
            "root",
            "root:component",
            [],
            [new("counter-alias", 1, null)]);
        var packaged = new WitDocument(
            [],
            [new(0, "filler", "example:filler@1.0.0", [], []), packagedApi],
            [packagedWorld],
            [
                Type(0, null, "{\"list\":\"u8\"}"),
                Type(1, "counter", "\"resource\"", 1),
                Type(2, null, "{\"handle\":{\"own\":1}}"),
            ],
            "{}");

        var projection = Create().Project(
            packaged,
            packagedWorld,
            source,
            application,
            sourceWorker);

        Assert.Equal("example:worker/application@1.0.0", projection.WorkerContract.World);
        Assert.Single(projection.WorkerContract.Exports);

        var mismatched = new WitDocument(
            [],
            [new(0, "filler", "example:filler@1.0.0", [], []), packagedApi],
            [packagedWorld],
            [
                Type(0, null, "{\"list\":\"u8\"}"),
                Type(1, "gauge", "\"resource\"", 1),
                Type(2, null, "{\"handle\":{\"own\":1}}"),
            ],
            "{}");

        AssertInvalid(
            () => Create().Project(
                mismatched,
                packagedWorld,
                source,
                application,
                sourceWorker),
            "not an exact export subset");
    }

    [Fact]
    public void RejectsDifferentResourceKindWhenFunctionSignatureMatches()
    {
        var sourceApi = new WitInterface(
            0,
            "counters",
            "example:worker@1.0.0",
            [],
            [new(
                "[static]counter.current",
                [],
                new WitTypeReference.Primitive("s32"),
                new("static", 0))]);
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new("counter-alias", 0, null)]);
        var sourceWorker = application with { Id = 1, Name = "worker" };
        var source = new WitDocument(
            [],
            [sourceApi],
            [application, sourceWorker],
            [Type(0, "counter", "\"resource\"", 0)],
            "{}");

        var packagedApi = new WitInterface(
            1,
            "counters",
            "example:worker@1.0.0",
            [],
            [new(
                "[static]counter.current",
                [],
                new WitTypeReference.Primitive("s32"),
                new("static", 1))]);
        var packagedWorld = new WitWorld(
            0,
            "root",
            "root:component",
            [],
            [new("counter-alias", 1, null)]);
        var packaged = new WitDocument(
            [],
            [new(0, "filler", "example:filler@1.0.0", [], []), packagedApi],
            [packagedWorld],
            [
                Type(0, null, "{\"list\":\"u8\"}"),
                Type(1, "gauge", "\"resource\"", 1),
            ],
            "{}");

        AssertInvalid(
            () => Create().Project(
                packaged,
                packagedWorld,
                source,
                application,
                sourceWorker),
            "not an exact export subset");
    }

    [Fact]
    public void RejectsApplicationWorldThatIsNotExactPackagedSubset()
    {
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new("value", null, Function(
                "value",
                [new("input", new WitTypeReference.Primitive("s32"))],
                new WitTypeReference.Primitive("s32")))]);
        var worker = new WitWorld(
            1,
            "worker",
            "example:worker@1.0.0",
            [],
            [new("value", null, Function(
                "value",
                [new("input", new WitTypeReference.Primitive("u32"))],
                new WitTypeReference.Primitive("s32")))]);
        var document = new WitDocument([], [], [application, worker], [], "{}");

        AssertInvalid(
            () => Create().Project(document, worker, application),
            "not an exact export subset");
    }

    [Fact]
    public void RequiresNamedApplicationAliasInSourceWorkerWorld()
    {
        var api = new WitInterface(
            0,
            "api",
            "example:worker@1.0.0",
            [],
            [Function("value", [], new WitTypeReference.Primitive("s32"))]);
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            [new("application-api", 0, null)]);
        var worker = new WitWorld(
            1,
            "worker",
            "example:worker@1.0.0",
            [],
            [new("different-api", 0, null)]);
        var document = new WitDocument([], [api], [application, worker], [], "{}");

        AssertInvalid(
            () => Create().Project(document, worker, application),
            "not an exact export subset");
    }

    [Fact]
    public void RejectsUnpairedReactorHostAndGuestInterfaces()
    {
        var reactorHost = new WitInterface(
            0,
            "reactor-host",
            "netwasm:runtime@1.0.0",
            [],
            [Function("watch", [], null), Function("cancel", [], null)]);
        var world = new WitWorld(
            0,
            "worker",
            "example:worker@1.0.0",
            [new("interface-0", 0, null)],
            [new("value", null, Function("value", [], null))]);

        AssertInvalid(
            () => Create().Project(new([], [reactorHost], [world], [], "{}"), world),
            "require exactly one reactor-guest export");
    }

    [Fact]
    public void RejectsDuplicateReactorHostImports()
    {
        var reactorHost = new WitInterface(
            0,
            "reactor-host",
            "netwasm:runtime@1.0.0",
            [],
            [Function("watch", [], null), Function("cancel", [], null)]);
        var reactorGuest = new WitInterface(
            1,
            "reactor-guest",
            "netwasm:runtime@1.0.0",
            [],
            [Function(
                "wake",
                [new("token", new WitTypeReference.Primitive("u32"))],
                null)]);
        var world = new WitWorld(
            0,
            "worker",
            "example:worker@1.0.0",
            [new("first", 0, null), new("second", 0, null)],
            [new("interface-1", 1, null)]);

        AssertInvalid(
            () => Create().Project(
                new([], [reactorHost, reactorGuest], [world], [], "{}"),
                world),
            "duplicate identity");
    }

    [Fact]
    public void RejectsDuplicateReactorGuestExports()
    {
        var reactorGuest = new WitInterface(
            0,
            "reactor-guest",
            "netwasm:runtime@1.0.0",
            [],
            [Function(
                "wake",
                [new("token", new WitTypeReference.Primitive("u32"))],
                null)]);
        var world = new WitWorld(
            0,
            "worker",
            "example:worker@1.0.0",
            [],
            [new("first", 0, null), new("second", 0, null)]);

        var packaged = new WitDocument([], [reactorGuest], [world], [], "{}");
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            []);
        var source = new WitDocument([], [], [application], [], "{}");

        AssertInvalid(
            () => Create().Project(
                packaged,
                world,
                source,
                application,
                application),
            "duplicated");
    }

    [Fact]
    public void RejectsDuplicateProjectedIdentities()
    {
        var api = new WitInterface(
            0,
            "api",
            "example:contracts@1.2.3",
            [],
            [Function("run", [], null)]);
        var world = new WitWorld(
            0,
            "command",
            "example:app@4.5.6",
            [new("first", 0, null), new("second", 0, null)],
            []);

        var exception = Assert.Throws<CompilerException>(() =>
            Create().Project(new([], [api], [world], [], "{}"), world));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("duplicate identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDefaultFunctionInventory()
    {
        var world = World(default);

        AssertInvalid(() => CreateUnchecked().Project(Document(world), world), "must be explicit");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(1)]
    public void RejectsInvalidInterfaceReference(int? interfaceId)
    {
        var world = World([new("api", interfaceId, null)]);

        AssertInvalid(() => CreateUnchecked().Project(Document(world), world), "invalid interface");
    }

    [Fact]
    public void RejectsMissingInterface()
    {
        var world = World([new("api", 0, null)]);
        var document = new WitDocument([], [null!], [world], [], "{}");

        AssertInvalid(() => CreateUnchecked().Project(document, world), "missing interface");
    }

    [Fact]
    public void RejectsDefaultInterfaceFunctions()
    {
        var world = World([new("api", 0, null)]);
        var definition = Interface(default);

        AssertInvalid(
            () => CreateUnchecked().Project(Document(world, definition), world),
            "functions must be explicit");
    }

    [Fact]
    public void RejectsNullWorldItemAndInterfaceFunction()
    {
        var nullItemWorld = World([null!]);
        Assert.Throws<ArgumentNullException>(() =>
            CreateUnchecked().Project(Document(nullItemWorld), nullItemWorld));

        var interfaceWorld = World([new("api", 0, null)]);
        Assert.Throws<ArgumentNullException>(() => CreateUnchecked().Project(
            Document(interfaceWorld, Interface([null!])),
            interfaceWorld));
    }

    [Fact]
    public void RejectsIncompleteWorldFunction()
    {
        var unnamed = World([new(" ", null, Function("ignored", [], null))]);
        AssertInvalid(() => CreateUnchecked().Project(Document(unnamed), unnamed), "incomplete");

        var defaultParameters = World([
            new("run", null, Function("ignored", default, null)),
        ]);
        AssertInvalid(
            () => CreateUnchecked().Project(Document(defaultParameters), defaultParameters),
            "incomplete");
    }

    [Fact]
    public void RejectsNullFunctionParameter()
    {
        var world = World([new("run", null, Function("ignored", [null!], null))]);

        Assert.Throws<ArgumentNullException>(() =>
            CreateUnchecked().Project(Document(world), world));
    }

    [Fact]
    public void RejectsIncompleteWorkerDetails()
    {
        var blankFunction = ExportWorld([
            new("run", null, Function(" ", [], null)),
        ]);
        AssertInvalid(
            () => CreateUnchecked().Project(Document(blankFunction), blankFunction),
            "worker export is incomplete");

        var blankParameter = ExportWorld([
            new("run", null, Function(
                "run",
                [new(" ", new WitTypeReference.Primitive("u32"))],
                null)),
        ]);
        AssertInvalid(
            () => CreateUnchecked().Project(Document(blankParameter), blankParameter),
            "parameter is incomplete");

    }

    [Fact]
    public void RejectsIncompletePackagedReactorGuest()
    {
        var guest = new WitInterface(
            0,
            "reactor-guest",
            "netwasm:runtime@1.0.0",
            [],
            [Function("other", [], null)]);
        var packagedWorld = new WitWorld(
            0,
            "worker",
            "example:worker@1.0.0",
            [],
            [new("guest", 0, null)]);
        var packaged = new WitDocument([], [guest], [packagedWorld], [], "{}");
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            []);
        var source = new WitDocument([], [], [application], [], "{}");

        AssertInvalid(
            () => Create().Project(
                packaged,
                packagedWorld,
                source,
                application,
                application),
            "incomplete");
    }

    [Fact]
    public void RejectsDefaultPackagedExportInventoryBeforeProjection()
    {
        var packagedWorld = new WitWorld(
            0,
            "worker",
            "example:worker@1.0.0",
            [],
            default);
        var packaged = new WitDocument([], [], [packagedWorld], [], "{}");
        var application = new WitWorld(
            0,
            "application",
            "example:worker@1.0.0",
            [],
            []);
        var source = new WitDocument([], [], [application], [], "{}");

        AssertInvalid(
            () => CreateUnchecked().Project(
                packaged,
                packagedWorld,
                source,
                application,
                application),
            "inventories must be explicit");
    }

    [Theory]
    [InlineData("missing-packaged-function")]
    [InlineData("missing-application-function")]
    [InlineData("item-name")]
    [InlineData("function-name")]
    [InlineData("kind")]
    [InlineData("resource-left")]
    [InlineData("resource-right")]
    [InlineData("parameters-left")]
    [InlineData("parameters-right")]
    [InlineData("parameter-count")]
    [InlineData("parameter-name")]
    [InlineData("parameter-type")]
    [InlineData("result-left")]
    [InlineData("result-right")]
    [InlineData("result-type")]
    public void RejectsApplicationRootFunctionContractDrift(string drift)
    {
        var u32 = new WitTypeReference.Primitive("u32");
        var packagedFunction = Function("run", [new("value", u32)], u32);
        var applicationFunction = packagedFunction;
        var packagedItem = new WitWorldItem("run", null, packagedFunction);
        var applicationItem = new WitWorldItem("run", null, applicationFunction);

        switch (drift)
        {
            case "missing-packaged-function":
                packagedItem = packagedItem with { Function = null };
                break;
            case "missing-application-function":
                applicationItem = applicationItem with { Function = null };
                break;
            case "item-name":
                packagedItem = packagedItem with { Name = "other" };
                break;
            case "function-name":
                applicationFunction = applicationFunction with { Name = "other" };
                break;
            case "kind":
                applicationFunction = applicationFunction with
                {
                    Kind = new("method"),
                };
                break;
            case "resource-left":
                packagedFunction = packagedFunction with { Kind = new("method") };
                applicationFunction = applicationFunction with { Kind = new("method", 0) };
                break;
            case "resource-right":
                packagedFunction = packagedFunction with { Kind = new("method", 0) };
                applicationFunction = applicationFunction with { Kind = new("method") };
                break;
            case "parameters-left":
                packagedFunction = packagedFunction with { Parameters = default };
                break;
            case "parameters-right":
                applicationFunction = applicationFunction with { Parameters = default };
                break;
            case "parameter-count":
                applicationFunction = applicationFunction with { Parameters = [] };
                break;
            case "parameter-name":
                applicationFunction = applicationFunction with
                {
                    Parameters = [new("other", u32)],
                };
                break;
            case "parameter-type":
                applicationFunction = applicationFunction with
                {
                    Parameters = [new("value", new WitTypeReference.Primitive("s32"))],
                };
                break;
            case "result-left":
                packagedFunction = packagedFunction with { Result = null };
                break;
            case "result-right":
                applicationFunction = applicationFunction with { Result = null };
                break;
            case "result-type":
                applicationFunction = applicationFunction with
                {
                    Result = new WitTypeReference.Primitive("s32"),
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(drift));
        }
        if (packagedItem.Function is not null)
        {
            packagedItem = packagedItem with { Function = packagedFunction };
        }
        if (applicationItem.Function is not null)
        {
            applicationItem = applicationItem with { Function = applicationFunction };
        }
        var packagedWorld = ExportWorld([packagedItem]);
        var applicationWorld = ExportWorld([applicationItem]);

        AssertInvalid(
            () => CreateUnchecked().Project(
                Document(packagedWorld),
                packagedWorld,
                Document(applicationWorld),
                applicationWorld,
                applicationWorld),
            "not an exact export subset");
    }

    [Theory]
    [InlineData("functions-left")]
    [InlineData("functions-right")]
    [InlineData("function-count")]
    [InlineData("function-name")]
    public void RejectsApplicationInterfaceFunctionInventoryDrift(string drift)
    {
        var run = Function("run", [], null);
        var packagedInterface = Interface([run]);
        var applicationInterface = Interface([run]);
        switch (drift)
        {
            case "functions-left":
                packagedInterface = packagedInterface with { Functions = default };
                break;
            case "functions-right":
                applicationInterface = applicationInterface with { Functions = default };
                break;
            case "function-count":
                applicationInterface = applicationInterface with { Functions = [] };
                break;
            case "function-name":
                applicationInterface = applicationInterface with
                {
                    Functions = [Function("other", [], null)],
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(drift));
        }
        var packagedWorld = ExportWorld([new("api", 0, null)]);
        var applicationWorld = ExportWorld([new("api", 0, null)]);
        var packaged = Document(packagedWorld, packagedInterface);
        var application = Document(applicationWorld, applicationInterface);

        AssertInvalid(
            () => CreateUnchecked().Project(
                packaged,
                packagedWorld,
                application,
                applicationWorld,
                applicationWorld),
            "not an exact export subset");
    }

    private static WitWorldFunctionProjector Create()
    {
        var interfaces = new WitInterfaceSpecifierFormatter();
        return new WitWorldFunctionProjector(
            new WitWorldValidator(),
            new WitWorldSpecifierFormatter(),
            interfaces,
            new WitTypeIdentityFormatter(interfaces),
            new WitJavaScriptNameFormatter(),
            WorkerTypes(interfaces));
    }

    private static WitWorldFunctionProjector CreateUnchecked()
    {
        var interfaces = new WitInterfaceSpecifierFormatter();
        return new(
            new NoOpWorldValidator(),
            new WitWorldSpecifierFormatter(),
            interfaces,
            new WitTypeIdentityFormatter(interfaces),
            new WitJavaScriptNameFormatter(),
            WorkerTypes(interfaces));
    }

    private static WitWorkerTypeProjector WorkerTypes(IWitInterfaceSpecifierFormatter interfaces) =>
        new WitWorkerTypeProjector(
            new WitWorkerTypeReferenceReader(WitWorkerReferenceComposition.CreateReaders()),
            new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter()), interfaces);

    private static WitWorld World(ImmutableArray<WitWorldItem> imports) =>
        new(0, "command", "example:app@1.0.0", imports, []);

    private static WitWorld ExportWorld(ImmutableArray<WitWorldItem> exports) =>
        new(0, "command", "example:app@1.0.0", [], exports);

    private static WitInterface Interface(ImmutableArray<WitFunction> functions) =>
        new(0, "api", "example:contracts@1.0.0", [], functions);

    private static WitDocument Document(WitWorld world, WitInterface? definition = null) =>
        new([], definition is null ? [] : [definition], [world], [], "{}");

    private static void AssertInvalid(Action action, string message)
    {
        var exception = Assert.Throws<CompilerException>(action);
        Assert.Contains(message, exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    private static WitFunction Function(
        string name,
        System.Collections.Immutable.ImmutableArray<WitParameter> parameters,
        WitTypeReference? result) =>
        new(name, parameters, result, new("freestanding"));

    private static WitTypeDefinition Type(
        int id,
        string? name,
        string json,
        int? owner = null)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), owner);
    }

    private sealed class NoOpWorldValidator : IWitWorldValidator
    {
        public void Validate(WitDocument document, WitWorld world)
        {
        }
    }
}
