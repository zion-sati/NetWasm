using System;
using System.Linq;

namespace NetWasm.Compiler.Core.Types;

public sealed class PreciseStaticConstructorClassifier(IMethodRepository methods) : IPreciseStaticConstructorClassifier
{
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public bool Classify(TypeDefinitionModel type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return !type.IsBeforeFieldInit && type.Methods.Any(key => _methods.GetMethod(key).Name == ".cctor");
    }
}
