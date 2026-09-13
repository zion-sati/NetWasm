using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ReachableObjectLayoutBuilder(
    ITypeRepository typeRepository,
    ReachableProgram program,
    IManagedObjectLayoutBuilder objectLayouts) : IReachableObjectLayoutBuilder
{
    private readonly ITypeRepository _typeRepository = typeRepository ??
        throw new ArgumentNullException(nameof(typeRepository));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly IManagedObjectLayoutBuilder _objectLayouts = objectLayouts ??
        throw new ArgumentNullException(nameof(objectLayouts));

    public void Build()
    {
        foreach (var type in _program.Types
                     .Where(key => _typeRepository.GetTypeDefinition(key).GenericArity == 0)
                     .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(key => key.MetadataToken))
        {
            _objectLayouts.Build(type);
        }
        foreach (var type in _program.RuntimeTypeDefinitions
                     .Where(key => _typeRepository.GetTypeDefinition(key).GenericArity > 0)
                     .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(key => key.MetadataToken))
        {
            _objectLayouts.Build(type);
        }
        foreach (var type in _program.ConstructedTypes
                     .OrderBy(type => type.CanonicalName, StringComparer.Ordinal))
        {
            _objectLayouts.Build(type);
        }
    }
}
