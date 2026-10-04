using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorSignatureComparer
{
    bool Compare(CliTypeIdentity left, CliTypeIdentity right, bool includeModifiers);
}
