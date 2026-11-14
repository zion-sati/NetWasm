using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

// Resolves UnsafeAccessorTypeAttribute names as the CLR does: an assembly-qualified name in that assembly, a
// name without one in the accessor's assembly and then in CoreLib. The program's assemblies are all there is
// to search; NetWasm does not load others at run time.
internal sealed class UnsafeAccessorTypeNameResolver : IUnsafeAccessorTypeNameResolver
{
    private readonly FrozenDictionary<(string Assembly, string FullName), TypeDefinitionModel> _types;
    private readonly AssemblyIdentity _coreLibrary;

    public UnsafeAccessorTypeNameResolver(IEnumerable<TypeDefinitionModel> types, AssemblyIdentity coreLibrary)
    {
        ArgumentNullException.ThrowIfNull(types);
        _types = types
            .GroupBy(type => (type.Key.Assembly.Name, type.FullName))
            .ToFrozenDictionary(group => group.Key, group => group.First());
        _coreLibrary = coreLibrary;
    }

    public TypeDefinitionModel? Resolve(string typeName, AssemblyIdentity accessorAssembly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var separator = typeName.IndexOf(',', StringComparison.Ordinal);
        var fullName = (separator < 0 ? typeName : typeName[..separator]).Trim();
        if (fullName.IndexOfAny(['[', ']', '*', '&']) >= 0)
            throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata,
                $"UnsafeAccessorTypeAttribute name '{typeName}' has generic arguments, an array, a pointer or a byref, which are not supported"));
        if (separator >= 0)
        {
            var assembly = typeName[(separator + 1)..].Split(',')[0].Trim();
            return _types.GetValueOrDefault((assembly, fullName));
        }
        return _types.GetValueOrDefault((accessorAssembly.Name, fullName)) ??
            _types.GetValueOrDefault((_coreLibrary.Name, fullName));
    }
}
