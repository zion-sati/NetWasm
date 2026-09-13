using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.Aliases;

public interface IWitAliasValidator
{
    void Validate(WitDocument document, WitTypeDefinition definition);
}
