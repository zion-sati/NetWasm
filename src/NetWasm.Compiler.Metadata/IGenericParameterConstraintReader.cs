using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.GenericConstraints;

namespace NetWasm.Compiler.Metadata;

public interface IGenericParameterConstraintReader
{
    ImmutableArray<GenericParameterConstraintModel> Read(EntityKey owner);
}
