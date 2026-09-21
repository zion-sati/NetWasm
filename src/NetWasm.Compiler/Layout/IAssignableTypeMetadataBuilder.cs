using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IAssignableTypeMetadataBuilder
{
    AssignableTypeMetadataLayout Build(CliTypeIdentity candidate);
}

internal readonly record struct AssignableTypeMetadataLayout(
    int Address,
    int Count);
