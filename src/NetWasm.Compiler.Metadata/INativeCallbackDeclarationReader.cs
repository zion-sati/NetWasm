using System.Reflection.Metadata;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public interface INativeCallbackDeclarationReader
{
    NativeCallbackDeclaration? Read(MetadataReader metadata, MethodDefinition method);
}
