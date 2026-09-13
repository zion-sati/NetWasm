using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IExplicitValueLayoutResolver
{
    ValueLayout Resolve(
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        ImmutableArray<FieldDefinitionModel> instanceFields);
}
