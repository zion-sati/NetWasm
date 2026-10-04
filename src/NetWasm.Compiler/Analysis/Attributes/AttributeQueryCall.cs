using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal enum AttributeQueryApiKind
{
    Instance,
    AttributeStatic,
    Extension,
}

internal sealed record AttributeQueryCall(
    AttributeQueryOperation Operation,
    int ArgumentCount,
    int? FilterArgument,
    CliTypeIdentity? GenericFilter,
    int? InheritArgument);
