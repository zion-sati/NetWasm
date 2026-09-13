using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Catalogs;

public sealed class WitInterfaceCatalogBuilderTests
{
    [Fact]
    public void BuildsDeterministicImportedInterfaceCatalogWithExactSignatures()
    {
        var zeta = Interface(0, "zeta",
            Function("z-last", [new("value", new WitTypeReference.Primitive("string"))], null),
            Function("a-first", [], new WitTypeReference.Primitive("u32")));
        var alpha = Interface(1, "alpha", Function("run", [], null));
        var exported = Function("main", [], null);
        var world = new WitWorld(0, "command", "wasi:cli@0.2.11",
            [new("zeta", 0, null), new("alpha", 1, null)],
            [new("run", null, exported)]);
        var document = new WitDocument([], [zeta, alpha], [world], [], "{\"canonical\":true}");

        var catalog = Create().Build(document, world);

        Assert.Equal("wasi:cli/command@0.2.11", catalog.World);
        Assert.Equal(document.NormalizedJson, catalog.NormalizedWitJson);
        Assert.Collection(catalog.Interfaces,
            contract =>
            {
                Assert.Equal("wasi:test/alpha@1.2.3", contract.Module);
                var function = Assert.Single(contract.Functions);
                Assert.Equal(contract.Module, function.Interface);
                Assert.Equal("run", function.Name);
                Assert.Empty(function.Parameters);
                Assert.Empty(function.Results);
            },
            contract =>
            {
                Assert.Equal("wasi:test/zeta@1.2.3", contract.Module);
                Assert.Collection(contract.Functions,
                    function =>
                    {
                        Assert.Equal("a-first", function.Name);
                        Assert.Empty(function.Parameters);
                        Assert.Equal(["u32"], function.Results);
                    },
                    function =>
                    {
                        Assert.Equal("z-last", function.Name);
                        Assert.Equal(["string"], function.Parameters);
                        Assert.Empty(function.Results);
                    });
            });
    }

    [Fact]
    public void RejectsMissingDependenciesAndInputsBeforeDelegation()
    {
        var validator = new RecordingWorldValidator();
        var builder = Create(validator);
        var world = new WitWorld(0, "command", "wasi:cli@0.2.11", [], []);
        var document = new WitDocument([], [], [world], [], "{}");

        Assert.Throws<ArgumentNullException>(() => new WitInterfaceCatalogBuilder(
            null!, new WitWorldSpecifierFormatter(), new WitInterfaceSpecifierFormatter(),
            new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter())));
        Assert.Throws<ArgumentNullException>(() => new WitInterfaceCatalogBuilder(
            validator, null!, new WitInterfaceSpecifierFormatter(),
            new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter())));
        Assert.Throws<ArgumentNullException>(() => new WitInterfaceCatalogBuilder(
            validator, new WitWorldSpecifierFormatter(), null!,
            new WitTypeIdentityFormatter(new WitInterfaceSpecifierFormatter())));
        Assert.Throws<ArgumentNullException>(() => new WitInterfaceCatalogBuilder(
            validator, new WitWorldSpecifierFormatter(), new WitInterfaceSpecifierFormatter(), null!));
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, world));
        Assert.Throws<ArgumentNullException>(() => builder.Build(document, null!));
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public void RejectsDefaultCollectionsAndMissingNormalizedSourceBeforeValidation()
    {
        var validator = new RecordingWorldValidator();
        var builder = Create(validator);
        var world = new WitWorld(0, "command", "wasi:cli@0.2.11", [], []);
        var document = new WitDocument([], [], [], [], "{}");

        AssertContractFailure(() => builder.Build(document with { Interfaces = default }, world));
        AssertContractFailure(() => builder.Build(document with { Types = default }, world));
        AssertContractFailure(() => builder.Build(document, world with { Imports = default }));
        AssertContractFailure(() => builder.Build(document, world with { Exports = default }));
        AssertContractFailure(() => builder.Build(document with { NormalizedJson = " " }, world));
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public void RejectsInvalidWorldItemsBeforeValidation()
    {
        var validator = new RecordingWorldValidator();
        var definition = Interface(0, "api", Function("run", [], null));
        var document = new WitDocument([], [definition], [], [], "{}");
        var invalidItems = new WitWorldItem[]
        {
            null!,
            new("missing", null, null),
            new("mixed", 0, Function("run", [], null)),
            new("unknown", 2, null),
        };

        foreach (var item in invalidItems)
        {
            AssertContractFailure(() => Create(validator).Build(
                document, new(0, "command", "wasi:cli@0.2.11", [item], [])));
        }
        AssertContractFailure(() => Create(validator).Build(
            document with { Interfaces = [null!] },
            new(0, "command", "wasi:cli@0.2.11", [], [new("missing", 0, null)])));
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public void RejectsDirectImportsDuplicateModulesAndInvalidFunctionInventories()
    {
        var noOp = new RecordingWorldValidator();
        var directWorld = new WitWorld(0, "command", "wasi:cli@0.2.11",
            [new("run", null, Function("run", [], null))], []);
        AssertContractFailure(() => Create(noOp).Build(
            new([], [], [], [], "{}"), directWorld));

        var first = Interface(0, "api", Function("run", [], null));
        var second = Interface(1, "api", Function("other", [], null));
        var duplicateWorld = new WitWorld(0, "command", "wasi:cli@0.2.11",
            [new("first", 0, null), new("second", 1, null)], []);
        AssertContractFailure(() => Create(noOp).Build(
            new([], [first, second], [], [], "{}"), duplicateWorld));

        var emptyWorld = new WitWorld(0, "command", "wasi:cli@0.2.11",
            [new("api", 0, null)], []);
        var emptyCatalog = Create(noOp).Build(
            new([], [Interface(0, "api")], [], [], "{}"), emptyWorld);
        Assert.Empty(Assert.Single(emptyCatalog.Interfaces).Functions);
        AssertInvalidInventory(noOp, InterfaceWithFunctions(default));
        AssertInvalidInventory(noOp, InterfaceWithFunctions([null!]));
        AssertInvalidInventory(noOp, Interface(0, "api", Function(" ", [], null)));
        AssertInvalidInventory(noOp, Interface(0, "api",
            Function("run", [], null), Function("run", [], null)));
        AssertInvalidInventory(noOp, Interface(0, "api",
            Function("run", default, null)));
        AssertInvalidInventory(noOp, Interface(0, "api",
            Function("run", [null!], null)));
        AssertInvalidInventory(noOp, Interface(0, "api",
            Function("run", [new("value", null!)], null)));
    }

    [Fact]
    public void StopsWhenWorldValidationFails()
    {
        var validator = new RecordingWorldValidator { Failure = new InvalidOperationException() };
        var definition = Interface(0, "api", Function("run", [], null));
        var world = new WitWorld(0, "command", "wasi:cli@0.2.11", [new("api", 0, null)], []);

        Assert.Same(validator.Failure, Assert.Throws<InvalidOperationException>(() =>
            Create(validator).Build(new([], [definition], [], [], "{}"), world)));
        Assert.Equal(1, validator.Calls);
    }

    private static void AssertInvalidInventory(
        IWitWorldValidator validator,
        WitInterface definition)
    {
        var world = new WitWorld(0, "command", "wasi:cli@0.2.11", [new("api", 0, null)], []);
        AssertContractFailure(() => Create(validator).Build(
            new([], [definition], [], [], "{}"), world));
    }

    private static IWitInterfaceCatalogBuilder Create(IWitWorldValidator? validator = null)
    {
        var interfaceFormatter = new WitInterfaceSpecifierFormatter();
        return Assert.IsAssignableFrom<IWitInterfaceCatalogBuilder>(new WitInterfaceCatalogBuilder(
            validator ?? new WitWorldValidator(),
            new WitWorldSpecifierFormatter(),
            interfaceFormatter,
            new WitTypeIdentityFormatter(interfaceFormatter)));
    }

    private static WitInterface Interface(
        int id,
        string name,
        params WitFunction[] functions) => new(
            id, name, "wasi:test@1.2.3", [], [.. functions]);

    private static WitInterface InterfaceWithFunctions(
        ImmutableArray<WitFunction> functions) => new(
            0, "api", "wasi:test@1.2.3", [], functions);

    private static WitFunction Function(
        string name,
        ImmutableArray<WitParameter> parameters,
        WitTypeReference? result) => new(name, parameters, result, new("freestanding"));

    private static void AssertContractFailure(Action action)
    {
        var exception = Assert.Throws<CompilerException>(action);
        Assert.Equal("NW1009", exception.Diagnostic.Id);
    }

    private sealed class RecordingWorldValidator : IWitWorldValidator
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }

        public void Validate(WitDocument document, WitWorld world)
        {
            Calls++;
            if (Failure is not null) throw Failure;
        }
    }
}
