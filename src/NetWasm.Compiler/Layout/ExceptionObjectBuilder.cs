using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ExceptionObjectBuilder(
    ITypeFinder typeFinder,
    ReachableProgram program,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state,
    IExceptionTypeNameResolver exceptionTypeNames) : IExceptionObjectBuilder
{
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));
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

    public void Build()
    {
        foreach (var kind in _program.ImplicitExceptions.Order())
        {
            _state.Cursor = ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment);
            var layout = GetObjectLayout(
                _typeFinder.FindType(_exceptionTypeNames.Resolve(kind)).Key);
            var bytes = new byte[layout.Size];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                bytes,
                layout.TypeId);
            _state.ExceptionObjects.Add(kind, _state.Cursor);
            _state.Segments.Add(new DataSegment(_state.Cursor, [.. bytes]));
            _state.Cursor += bytes.Length;
        }
    }

    private ObjectLayout GetObjectLayout(EntityKey type) =>
        _types.Objects.TryGetValue(type, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"object layout for '{type}' was not generated"));

}
