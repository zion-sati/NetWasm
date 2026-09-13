using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ImplicitObjectLayoutBuilder(
    ITypeFinder types,
    ITypeRepository typeRepository,
    ReachableProgram program,
    IManagedObjectLayoutBuilder objectLayouts,
    IExceptionTypeNameResolver exceptionTypeNames,
    ManagedTypeLayoutBuildState state) : IImplicitObjectLayoutBuilder
{
    private readonly ITypeFinder _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly IManagedObjectLayoutBuilder _objectLayouts = objectLayouts ??
        throw new ArgumentNullException(nameof(objectLayouts));
    private readonly IExceptionTypeNameResolver _exceptionTypeNames = exceptionTypeNames ??
        throw new ArgumentNullException(nameof(exceptionTypeNames));
    private readonly ITypeRepository _typeRepository = typeRepository ??
        throw new ArgumentNullException(nameof(typeRepository));
    private readonly ManagedTypeLayoutBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));

    public void Build()
    {
        if (!_program.StringLiterals.IsEmpty ||
            _state.Objects.Keys.Any(type => _typeRepository.GetTypeDefinition(type).IsEnum))
        {
            _objectLayouts.Build(_types.FindType("System.String").Key);
        }
        foreach (var kind in _program.ImplicitExceptions.Order())
        {
            _objectLayouts.Build(
                _types.FindType(_exceptionTypeNames.Resolve(kind)).Key);
        }
    }
}
