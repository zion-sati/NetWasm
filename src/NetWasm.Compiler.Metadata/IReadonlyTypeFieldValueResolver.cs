using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IReadonlyTypeFieldValueResolver
{
    CliTypeIdentity? Resolve(EntityKey field);
    CliTypeIdentity? Resolve(FieldInstanceModel field);
}
