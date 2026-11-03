using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ExceptionObjectBuilder(
    ITypeFinder typeFinder,
    IFieldRepository fields,
    ReachableProgram program,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state,
    IExceptionTypeNameResolver exceptionTypeNames) : IExceptionObjectBuilder
{
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly WasmTargetLayout _target = target ??
        throw new ArgumentNullException(nameof(target));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly IExceptionTypeNameResolver _exceptionTypeNames = exceptionTypeNames ??
        throw new ArgumentNullException(nameof(exceptionTypeNames));

    // PreallocatedOutOfMemoryException.Instance, frozen in the image: the
    // OutOfMemoryException thrown when not even a new one fits, as
    // new OutOfMemoryException(message: null) makes it. What a program stores in
    // it, as Data, is reachable from its fields, which are roots like a static
    // field's.
    public void Build()
    {
        if (!_program.ImplicitExceptions.Contains(ManagedExceptionKind.OutOfMemory)) return;
        _state.Cursor = ManagedTypeLayoutCompiler.Align(
            _state.Cursor,
            _target.ObjectReferenceAlignment);
        var layout = GetObjectLayout(
            _typeFinder.FindType(_exceptionTypeNames.Resolve(ManagedExceptionKind.OutOfMemory)).Key);
        var bytes = new byte[layout.Size];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            bytes,
            layout.TypeId);
        if (HResultOffset() is { } hResult)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(hResult),
                RuntimeExceptionGetters.OutOfMemoryHResult);
        }
        _state.ExceptionObjects.Add(ManagedExceptionKind.OutOfMemory, _state.Cursor);
        _state.Segments.Add(new DataSegment(_state.Cursor, [.. bytes]));
        foreach (var referenceOffset in layout.ReferenceOffsets)
            _state.StaticRoots.Add(_state.Cursor + referenceOffset);
        _state.Cursor += bytes.Length;
    }

    // System.Exception._hResult, unless no layout keeps it.
    private int? HResultOffset()
    {
        var field = _typeFinder.FindType("System.Exception").Fields
            .Select(_fields.GetField)
            .SingleOrDefault(static candidate => candidate.Name == "_hResult");
        return field is not null && _types.ValueLayoutState.Fields.TryGetValue(field.Key, out var layout)
            ? layout.Offset
            : null;
    }

    private ObjectLayout GetObjectLayout(EntityKey type) =>
        _types.Objects.TryGetValue(type, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"object layout for '{type}' was not generated"));

}
