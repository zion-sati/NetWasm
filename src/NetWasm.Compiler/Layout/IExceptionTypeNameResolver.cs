using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IExceptionTypeNameResolver
{
    string Resolve(ManagedExceptionKind kind);
}
