using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ISignatureTypeComparer
{
    bool Compare(CliTypeIdentity left, CliTypeIdentity right);
}
