namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeDeclarationValidator
{
    void Validate(MethodInstanceModel method);
}
