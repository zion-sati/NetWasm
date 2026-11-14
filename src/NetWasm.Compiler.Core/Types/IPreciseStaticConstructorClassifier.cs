namespace NetWasm.Compiler.Core.Types;

public interface IPreciseStaticConstructorClassifier
{
    // Whether the type's static constructor runs at a precise point (ECMA-335 II.10.5.3.2): it has one and is not
    // beforefieldinit. Otherwise an access to one of its static fields alone need not initialize it.
    bool Classify(TypeDefinitionModel type);
}
