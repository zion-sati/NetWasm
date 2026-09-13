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
    public void RequiresEveryCollaborator(int missing)
    {
        var worlds = new NoOpWorldValidator();
        var worldSpecifiers = new WitWorldSpecifierFormatter();
        var interfaceSpecifiers = new WitInterfaceSpecifierFormatter();
        var types = new WitTypeIdentityFormatter(interfaceSpecifiers);

        Assert.Throws<ArgumentNullException>(() => new WitWorldFunctionProjector(
            missing == 0 ? null! : worlds,
            missing == 1 ? null! : worldSpecifiers,
            missing == 2 ? null! : interfaceSpecifiers,
            missing == 3 ? null! : types));
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

    private static WitWorldFunctionProjector Create()
    {
        var interfaces = new WitInterfaceSpecifierFormatter();
        return new WitWorldFunctionProjector(
            new WitWorldValidator(),
            new WitWorldSpecifierFormatter(),
            interfaces,
            new WitTypeIdentityFormatter(interfaces));
    }

    private static WitWorldFunctionProjector CreateUnchecked()
    {
        var interfaces = new WitInterfaceSpecifierFormatter();
        return new(
            new NoOpWorldValidator(),
            new WitWorldSpecifierFormatter(),
            interfaces,
            new WitTypeIdentityFormatter(interfaces));
    }

    private static WitWorld World(ImmutableArray<WitWorldItem> imports) =>
        new(0, "command", "example:app@1.0.0", imports, []);

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

    private sealed class NoOpWorldValidator : IWitWorldValidator
    {
        public void Validate(WitDocument document, WitWorld world)
        {
        }
    }
}
