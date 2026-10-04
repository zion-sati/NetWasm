using System.Reflection.Metadata;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public interface INativeImportDeclarationReader
{
    NativeImportDeclaration? Read(MetadataReader metadata, MethodDefinition method);
}
