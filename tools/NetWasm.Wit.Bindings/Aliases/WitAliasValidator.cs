using System;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.Aliases;

public sealed class WitAliasValidator(IWitBindingSyntaxFormatter syntax) : IWitAliasValidator
{
    private readonly IWitBindingSyntaxFormatter _syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));

    public void Validate(WitDocument document, WitTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);
        _ = _syntax.Format(new WitBindingSyntaxRequest.TypeDefinitionName(document, definition));
    }
}
