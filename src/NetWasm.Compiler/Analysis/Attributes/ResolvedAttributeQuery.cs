using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed record ResolvedAttributeQuery(
    AttributeQueryCall Call,
    CliTypeIdentity Target,
    CliTypeIdentity Filter,
    bool? InheritConstant);
