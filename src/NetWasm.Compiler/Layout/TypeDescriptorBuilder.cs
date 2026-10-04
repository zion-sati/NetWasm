using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class TypeDescriptorBuilder(
    ITypeIdentityResolver identities,
    ITypeDefinitionResolver typeDefinitions,
    IMetadataIdentityBaseTypeResolver baseTypes,
    IObjectLayoutResolver objectLayouts,
    ReachableProgram program,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state,
    IStaticReferenceBitmapBuilder bitmaps,
    IAssignableTypeMetadataBuilder assignableTypes,
    IAssemblyIdentityFormatter? assemblyNames = null) : ITypeDescriptorBuilder
{
    internal TypeDescriptorBuilder(
        ITypeIdentityResolver identities,
        ITypeDefinitionResolver typeDefinitions,
        IMetadataIdentityBaseTypeResolver baseTypes,
        IObjectLayoutResolver objectLayouts,
        ReachableProgram program,
        ManagedTypeLayouts types,
        WasmTargetLayout target,
        ManagedStaticDataBuildState state,
        IStaticReferenceBitmapBuilder bitmaps) :
        this(
            identities,
            typeDefinitions,
            baseTypes,
            objectLayouts,
            program,
            types,
            target,
            state,
            bitmaps,
            EmptyAssignableTypeMetadataBuilder.Instance)
    {
    }

    private readonly ITypeIdentityResolver _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMetadataIdentityBaseTypeResolver _baseTypes = baseTypes ??
        throw new ArgumentNullException(nameof(baseTypes));
    private readonly IObjectLayoutResolver _objectLayouts = objectLayouts ??
        throw new ArgumentNullException(nameof(objectLayouts));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly WasmTargetLayout _target = target ??
        throw new ArgumentNullException(nameof(target));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly IStaticReferenceBitmapBuilder _bitmaps = bitmaps ??
        throw new ArgumentNullException(nameof(bitmaps));
    private readonly IAssignableTypeMetadataBuilder _assignableTypes = assignableTypes ??
        throw new ArgumentNullException(nameof(assignableTypes));
    private readonly IAssemblyIdentityFormatter? _assemblyNames = assemblyNames;

    public void Build()
    {
        foreach (var (type, layout) in _types.Objects.OrderBy(pair => pair.Value.TypeId))
        {
            _state.Cursor = ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment);
            var bitCount = DivideRoundUp(layout.Size, _target.ObjectReferenceSize);
            var bitmap = _bitmaps.Build(
                layout.ReferenceOffsets,
                bitCount,
                _target.ObjectReferenceSize);
            var bitmapAddress = _state.Cursor;
            AddSegment(bitmap);
            var typeIdentity = _identities.GetTypeIdentity(type);
            var assignableTypes = _assignableTypes.Build(typeIdentity);
            var descriptor = new TypeDescriptorLayout(
                type,
                layout.TypeId,
                GetBaseTypeId(typeIdentity),
                layout.Size,
                bitmapAddress,
                bitCount,
                _program.Finalizers.TryGetValue(typeIdentity, out var finalizer)
                    ? finalizer
                    : null)
            {
                AssignableTypeIdsAddress = assignableTypes.Address,
                AssignableTypeIdCount = assignableTypes.Count,
                IsInterface = _typeDefinitions.ResolveTypeIdentity(typeIdentity).IsInterface,
            };
            _state.TypeDescriptors.Add(descriptor);
            if (_program.RequiresTypeFacts)
            {
                _state.PendingTypeFacts.Add(new PendingRuntimeTypeFacts(
                    typeIdentity,
                    _typeDefinitions.ResolveTypeIdentity(typeIdentity),
                    descriptor.TypeId,
                    descriptor.BaseTypeId,
                    descriptor.AssignableTypeIdsAddress,
                    descriptor.AssignableTypeIdCount,
                    _program.DelegateInvokeDescriptors.TryGetValue(
                        typeIdentity.CanonicalName,
                        out var delegateInvoke)
                            ? delegateInvoke.CanonicalName
                            : null,
                    _program.TypeNamePayload != RuntimeTypeNamePayload.None
                        ? RuntimeTypeNameFormatter.Format(
                            typeIdentity,
                            _typeDefinitions,
                            _assemblyNames ?? throw new CompilerException(
                                new CompilerDiagnostic(
                                    DiagnosticCode.RuntimeContract,
                                    "runtime type names require an assembly identity formatter")),
                            _program.TypeNamePayload)
                        : null));
            }
        }
    }

    private int GetBaseTypeId(CliTypeIdentity type)
    {
        var baseType = _baseTypes.GetBaseType(type);
        if (baseType is null || baseType.ContainsGenericParameters)
        {
            // Open generic definitions are retained for runtime metadata only. A base such as
            // Base<T> has no physical object layout until T is closed; closed descendants use
            // ConstructedTypeDescriptorBuilder and retain their exact constructed base instead.
            return 0;
        }
        return _objectLayouts.Resolve(baseType).TypeId;
    }

    private void AddSegment(byte[] data)
    {
        _state.Segments.Add(new DataSegment(_state.Cursor, [.. data]));
        _state.Cursor += data.Length;
    }

    private static int DivideRoundUp(int value, int divisor)
    {
        checked
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
