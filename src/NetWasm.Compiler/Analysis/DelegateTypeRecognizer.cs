using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DelegateTypeRecognizer(
    ITypeDefinitionResolver types,
    IBaseTypeResolver baseTypes) : IDelegateTypeRecognizer
{
    public bool Recognize(CliTypeIdentity type)
    {
        if (type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
        {
            return false;
        }

        for (var current = type;
             current is not null;
             current = baseTypes.Resolve(current))
        {
            var name = types.ResolveTypeIdentity(current).FullName;
            if (name is "System.Delegate" or "System.MulticastDelegate")
            {
                return true;
            }
        }

        return false;
    }
}
