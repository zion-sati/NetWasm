using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IModuleInitializerResolver
{
    bool TryResolve(AssemblyIdentity assembly, out MethodDefinitionModel initializer);
}

/// <summary>Indexes ordinary C# module initializers by their defining assembly.</summary>
internal sealed class ModuleInitializerResolver : IModuleInitializerResolver
{
    private readonly Dictionary<AssemblyIdentity, MethodDefinitionModel> _initializers;

    internal ModuleInitializerResolver(
        IReadOnlyList<TypeDefinitionModel> types,
        IReadOnlyList<MethodDefinitionModel> methods)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(methods);
        var moduleTypes = types
            .Where(type => type.Name == "<Module>" && string.IsNullOrEmpty(type.Namespace))
            .ToDictionary(type => type.Key);
        var initializers = new Dictionary<AssemblyIdentity, MethodDefinitionModel>();
        foreach (var method in methods)
        {
            if (method.Name != ".cctor" || !moduleTypes.ContainsKey(method.DeclaringType))
            {
                continue;
            }

            if (!initializers.TryAdd(method.Key.Assembly, method))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    $"assembly '{method.Key.Assembly.Name}' contains more than one module initializer"));
            }
        }

        _initializers = initializers;
    }

    public bool TryResolve(AssemblyIdentity assembly, out MethodDefinitionModel initializer)
    {
        return _initializers.TryGetValue(assembly, out initializer!);
    }
}
