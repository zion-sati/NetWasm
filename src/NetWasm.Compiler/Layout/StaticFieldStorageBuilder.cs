using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticFieldStorageBuilder(
    ITypeRepository typeRepository,
    IFieldRepository fields,
    ReachableProgram program,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IStaticFieldStorageBuilder
{
    private readonly ITypeRepository _typeRepository = typeRepository ??
        throw new ArgumentNullException(nameof(typeRepository));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));

    public void Build()
    {
        foreach (var fieldKey in _program.Fields
                     .Where(key =>
                         _fields.GetField(key).IsStatic &&
                         _typeRepository.GetTypeDefinition(
                             _fields.GetField(key).DeclaringType).GenericArity == 0)
                     .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(key => key.MetadataToken))
        {
            var field = _fields.GetField(fieldKey);
            AddStaticField(field, GetValueLayout(field.SignatureType));
        }
        foreach (var field in _program.ConstructedFields.Values
                     .Where(field => field.Definition.IsStatic)
                     .OrderBy(field => field.CanonicalName, StringComparer.Ordinal))
        {
            var storage = GetValueLayout(field.FieldType);
            _state.Cursor = ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                storage.Alignment);
            _state.ConstructedStaticFields.Add(
                field.CanonicalName,
                new StaticFieldLayout(_state.Cursor));
            AddRoots(storage);
            _state.Cursor += storage.Size;
        }
    }

    private void AddStaticField(FieldDefinitionModel field, ValueLayout storage)
    {
        _state.Cursor = ManagedTypeLayoutCompiler.Align(_state.Cursor, storage.Alignment);
        _state.StaticFields.Add(field.Key, new StaticFieldLayout(_state.Cursor));
        if (!field.InitialData.IsDefaultOrEmpty)
        {
            if (field.InitialData.Length > storage.Size)
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    $"field '{field.Key}' initializer exceeds its static storage"));
            }
            _state.Segments.Add(new DataSegment(_state.Cursor, field.InitialData));
        }
        AddRoots(storage);
        _state.Cursor += storage.Size;
    }

    private void AddRoots(ValueLayout storage)
    {
        foreach (var referenceOffset in storage.ReferenceOffsets)
            _state.StaticRoots.Add(_state.Cursor + referenceOffset);
    }

    private ValueLayout GetValueLayout(CliTypeIdentity type) =>
        _types.ValueLayoutState.Values.TryGetValue(type, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"value layout for '{type}' was not generated"));
}
