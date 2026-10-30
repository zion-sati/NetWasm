using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

// The Type object of every type a reachable method materializes as a constant
// (typeof), laid out as static data the way string literals are. The runtime
// adopts them at startup as those types' objects, so GetType returns the same
// object, and typeof needs neither an allocation nor a runtime call.
internal sealed class TypeObjectDataBuilder(
    ITypeFinder typeFinder,
    ReachableProgram program,
    ManagedTypeLayouts types,
    IObjectLayoutResolver objectLayouts,
    ITypeOperandResolver typeOperands,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state) : ITypeObjectDataBuilder
{
    private readonly ITypeFinder _typeFinder = typeFinder ?? throw new ArgumentNullException(nameof(typeFinder));
    private readonly ReachableProgram _program = program ?? throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IObjectLayoutResolver _objectLayouts = objectLayouts ?? throw new ArgumentNullException(nameof(objectLayouts));
    private readonly ITypeOperandResolver _typeOperands = typeOperands ?? throw new ArgumentNullException(nameof(typeOperands));
    private readonly WasmTargetLayout _target = target ?? throw new ArgumentNullException(nameof(target));
    private readonly ManagedStaticDataBuildState _state = state ?? throw new ArgumentNullException(nameof(state));

    public void Build()
    {
        // A GetType whose receiver's type is unknown reads the runtime's Type
        // object cache through this word before it asks the runtime. It stays
        // zero, with no data, until the runtime initializer stores the address.
        if (_program.Methods.Values.Concat(_program.ConstructedMethods.Values).Any(ReadsObjectType))
        {
            _state.Cursor = ManagedTypeLayoutCompiler.Align(_state.Cursor, _target.ObjectReferenceAlignment);
            _state.TypeObjectCacheAddress = _state.Cursor;
            _state.Cursor += _target.ObjectReferenceSize;
        }
        var materialized = _program.Methods.Values.Concat(_program.ConstructedMethods.Values)
            .SelectMany(Materialized)
            .DistinctBy(type => type.CanonicalName)
            .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        if (materialized.Length == 0)
        {
            return;
        }

        var facade = _types.Objects.TryGetValue(_typeFinder.FindType("System.Type").Key, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "object layout for 'System.Type' was not generated"));
        var size = ManagedTypeLayoutCompiler.Align(facade.Size, _target.ObjectReferenceAlignment);
        _state.Cursor = ManagedTypeLayoutCompiler.Align(_state.Cursor, _target.ObjectReferenceAlignment);
        _state.TypeObjectTableAddress = _state.Cursor;
        _state.TypeObjectCount = materialized.Length;
        _state.TypeObjectSize = size;
        foreach (var type in materialized)
        {
            var bytes = new byte[size];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, facade.TypeId);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(_target.ObjectHeaderSize),
                _objectLayouts.Resolve(type).TypeId);
            _state.TypeObjects.Add(type.CanonicalName, _state.Cursor);
            _state.Segments.Add(new DataSegment(_state.Cursor, [.. bytes]));
            _state.Cursor += size;
        }
    }

    private static bool ReadsObjectType(ManagedMethodBody body) =>
        body.ControlFlow.Graph.Blocks
            .SelectMany(static block => block.Instructions)
            .Any(static instruction => instruction.Operation == CilOperation.GetObjectType &&
                !CilTypeMaterialization.UsesStaticTypeObject(instruction));

    private IEnumerable<CliTypeIdentity> Materialized(ManagedMethodBody body) =>
        body.ControlFlow.Graph.Blocks
            .SelectMany(static block => block.Instructions)
            .Where(CilTypeMaterialization.UsesStaticTypeObject)
            .Select(instruction => _typeOperands.Resolve(instruction, body.Method));
}
