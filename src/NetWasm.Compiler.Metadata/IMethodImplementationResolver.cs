using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodImplementationResolver
{
    ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(
        CliTypeIdentity type);
}
