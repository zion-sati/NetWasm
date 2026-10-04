namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeCallbackDeclarationValidator
{
    void ValidateAddressTarget(MethodInstanceModel method);
}
