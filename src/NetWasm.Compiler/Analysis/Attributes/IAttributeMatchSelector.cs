using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeMatchSelector
{
    ImmutableArray<CustomAttributeDescriptor> Select(CliTypeIdentity target, CliTypeIdentity filter,
        bool inherit, bool existenceOnly);
}
