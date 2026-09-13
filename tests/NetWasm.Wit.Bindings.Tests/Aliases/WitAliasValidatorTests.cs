using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Wit.Bindings.Aliases;

namespace NetWasm.Wit.Bindings.Tests.Aliases;

public sealed class WitAliasValidatorTests
{
    [Fact]
    public void ValidateDelegatesTheExactUnderlyingReference()
    {
        using var json = JsonDocument.Parse("""{"list":"u8"}""");
        var document = new WitDocument(
            ImmutableArray<WitPackage>.Empty,
            ImmutableArray<WitInterface>.Empty,
            ImmutableArray<WitWorld>.Empty,
            ImmutableArray<WitTypeDefinition>.Empty,
            "{}");
        var syntax = new RecordingSyntaxFormatter();
        IWitAliasValidator[] validators = [new WitAliasValidator(syntax)];

        var definition = new WitTypeDefinition(1, "alias", json.RootElement.Clone(), 0);

        validators[0].Validate(document, definition);

        var request = Assert.IsType<WitBindingSyntaxRequest.TypeDefinitionName>(syntax.Request);
        Assert.Same(document, request.Document);
        Assert.Same(definition, request.Definition);
    }

    [Fact]
    public void ValidateRejectsMissingCollaboratorsAndDocument()
    {
        Assert.Throws<ArgumentNullException>(() => new WitAliasValidator(null!));

        IWitAliasValidator[] validators = [new WitAliasValidator(new RecordingSyntaxFormatter())];
        Assert.Throws<ArgumentNullException>(() => validators[0].Validate(null!, null!));
        Assert.Throws<ArgumentNullException>(() => validators[0].Validate(
            new WitDocument(
                ImmutableArray<WitPackage>.Empty,
                ImmutableArray<WitInterface>.Empty,
                ImmutableArray<WitWorld>.Empty,
                ImmutableArray<WitTypeDefinition>.Empty,
                string.Empty),
            null!));
    }

    private sealed class RecordingSyntaxFormatter : IWitBindingSyntaxFormatter
    {
        public WitBindingSyntaxRequest? Request { get; private set; }

        public string Format(WitBindingSyntaxRequest request)
        {
            Request = request;
            return "byte[]";
        }
    }
}
