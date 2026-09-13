using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.TypeDefinitions;

public interface IWitTypeDefinitionClassifier
{
    WitTypeDefinitionCategory Classify(WitTypeDefinition definition);
}
