using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class DelegateTypeRecognizerFactory : IDelegateTypeRecognizerFactory
{
    public IDelegateTypeRecognizer Create(
        ITypeDefinitionResolver typeDefinitions,
        IBaseTypeResolver baseTypes) =>
        new DelegateTypeRecognizer(typeDefinitions, baseTypes);
}
