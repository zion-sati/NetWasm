using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IManagedExecutableArgumentFactoryResolver
{
    EntityKey? Resolve(
        CompilerEntryPointKind kind,
        MethodDefinitionModel entryPoint,
        ITypeFinder types,
        IMethodRepository methods);
}
