// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
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
        var spanAlignments = new Dictionary<EntityKey, int>();
        foreach (var method in _program.Methods.Values.Concat(_program.ConstructedMethods.Values))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operation is not (
                    CilOperation.CreateReadOnlySpanFromRva or
                    CilOperation.LoadStaticDataElement))
                    continue;
                if (instruction.Operand is not CilOperand.ReadOnlySpanData span)
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.RuntimeContract,
                        "RVA span creation metadata is missing during static placement."));
                var alignment = GetValueLayout(span.ElementType).Alignment;
                var key = span.Field.Definition.Key;
                spanAlignments.TryGetValue(key, out var existing);
                spanAlignments[key] = Math.Max(existing, alignment);
            }
        }
        foreach (var fieldKey in _program.Fields
                     .Where(key =>
                         _fields.GetField(key).IsStatic &&
                         _typeRepository.GetTypeDefinition(
                             _fields.GetField(key).DeclaringType).GenericArity == 0)
                     .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(key => key.MetadataToken))
        {
            var field = _fields.GetField(fieldKey);
            AddStaticField(field, GetValueLayout(field.SignatureType),
                spanAlignments.GetValueOrDefault(field.Key));
        }
        foreach (var field in _program.ConstructedFields.Values
                     .Where(field => field.Definition.IsStatic)
                     .OrderBy(field => field.CanonicalName, StringComparer.Ordinal))
        {
            var storage = GetValueLayout(field.FieldType);
            if (!field.Definition.InitialData.IsDefaultOrEmpty)
            {
                // RVA data belongs to the module, even for a generic declaring type.
                if (!_state.StaticFields.ContainsKey(field.Definition.Key))
                    AddStaticField(field.Definition, storage,
                        spanAlignments.GetValueOrDefault(field.Definition.Key));
                _state.ConstructedStaticFields.Add(
                    field.CanonicalName, _state.StaticFields[field.Definition.Key]);
                continue;
            }
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

    private void AddStaticField(
        FieldDefinitionModel field,
        ValueLayout storage,
        int requiredAlignment)
    {
        _state.Cursor = ManagedTypeLayoutCompiler.Align(
            _state.Cursor, Math.Max(storage.Alignment, requiredAlignment));
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
