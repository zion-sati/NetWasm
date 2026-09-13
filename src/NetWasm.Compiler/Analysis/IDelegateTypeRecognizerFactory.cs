using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IDelegateTypeRecognizerFactory
{
    IDelegateTypeRecognizer Create(
        ITypeDefinitionResolver typeDefinitions,
        IBaseTypeResolver baseTypes);
}
