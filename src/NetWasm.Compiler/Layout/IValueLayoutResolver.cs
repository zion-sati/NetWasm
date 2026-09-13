using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IValueLayoutResolver
{
    ValueLayout Resolve(CliTypeIdentity type);

    ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition);
}
