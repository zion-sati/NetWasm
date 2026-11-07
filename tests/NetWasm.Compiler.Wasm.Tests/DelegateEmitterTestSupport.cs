using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// Delegate types, targets and bindings the delegate function emitters' tests share.
internal static class DelegateEmitterTestSupport
{
    internal static CliTypeIdentity CallbackType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false);

    internal static MethodInstanceModel CreateInvoke(
        CliTypeIdentity callbackType,
        CliTypeIdentity returnType,
        params CliTypeIdentity[] parameters)
    {
        var definition = new MethodDefinitionModel(
            Key(0x06000020),
            TypeKey,
            "Invoke",
            false,
            new MethodSignatureModel(returnType, [.. parameters]),
            1);
        return new(definition, callbackType, [], definition.Signature);
    }

    internal static MethodInstanceModel CreateInvoke(
        CliTypeIdentity callbackType,
        MethodSignatureModel signature) =>
        CreateInvoke(callbackType, signature.ReturnSignatureType,
            [.. signature.ParameterSignatureTypes]);

    internal static MethodInstanceModel CreateTarget(
        MethodSignatureModel signature,
        string name,
        bool isStatic,
        EntityKey key,
        bool isValueType = false)
    {
        var definition = new MethodDefinitionModel(
            key,
            TypeKey,
            name,
            isStatic,
            signature,
            1);
        return new(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", name, isValueType),
            [],
            signature);
    }

    internal static DelegateInvokeTarget CreateTargetProgram(
        MethodInstanceModel invoke,
        params MethodInstanceModel[] targets) =>
        new(
            [.. targets.Select(target => CreateBinding(invoke, target))], Lookups.NoEntries<string, StaticInitializerGuard>());

    internal static FunctionIndexResolver CreateResolver(
        CliTypeIdentity callbackType,
        params MethodInstanceModel[] targets)
    {
        var program = new FakeProgram();
        return new FunctionIndexResolver(
            program,
            program,
            new FunctionIndexMap(
                targets.ToImmutableDictionary(
                    target => target.Definition.Key,
                    target => new WasmFunctionIndex(
                        (50 + target.Definition.Key.MetadataToken) & 0xff)),
                [],
                ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                    callbackType.CanonicalName,
                    new WasmFunctionIndex(40)),
                [])
            {
                ManagedThrowFunctionIndex = OptionalFunctionIndex.At(ManagedThrowFunction),
            });
    }

    internal static ManagedDelegateBinding CreateBinding(
        MethodInstanceModel invoke,
        MethodInstanceModel target) => new(
        new ManagedMethodIdentity(invoke.CanonicalName),
        new ManagedMethodIdentity(target.CanonicalName),
        invoke,
        target,
        [.. invoke.Signature.ParameterSignatureTypes.Select((parameter, index) =>
            new ManagedDelegateValueBinding(
                parameter,
                target.Signature.ParameterSignatureTypes[index],
                ManagedDelegateAdaptation.Identity))],
        new ManagedDelegateValueBinding(
            target.Signature.ReturnSignatureType,
            invoke.Signature.ReturnSignatureType,
            ManagedDelegateAdaptation.Identity));

    // The local declarations a generated function's body starts with.
    internal static byte[] LocalDeclarations(WasmTarget target, params CliValueKind[] locals)
    {
        var buffer = new WasmBinaryBuffer();
        WasmLocalDeclarationWriter.Write(new WasmBinaryWriter(buffer), locals, WasmTargetLayout.For(target));
        return new WasmBinarySnapshotReader(buffer).Read();
    }

    internal static WasmInstruction LocalGet(int local) =>
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local));

    internal static WasmInstruction LocalSet(int local) =>
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local));

    internal static WasmInstruction Call(int function) =>
        WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)function));

    internal static int RuntimeImport(RuntimeImportSymbol symbol) =>
        WasmRuntimeImports.CreateCatalog().Resolve(symbol);

    internal sealed class ReferenceValueLayoutProvider(
        CliTypeIdentity referencedType) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            type.Equals(referencedType)
                ? new(type, 16, 8, [0, 8])
                : new(type, 8, 8, []);
    }

    internal sealed class FixedValueLayoutProvider(int alignment) : IValueLayoutProvider
    {
        public CliTypeIdentity? RequestedType { get; private set; }

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            RequestedType = type;
            return new(type, alignment, alignment, []);
        }
    }

    internal sealed class RecordingWriterFactory : IGeneratedFunctionWriterFactory
    {
        public RecordingInstructionWriter Instructions { get; } = new();

        public GeneratedFunctionWriterLease Create()
        {
            var buffer = new WasmBinaryBuffer();
            return new(new WasmBinaryWriter(buffer), new WasmBinarySnapshotReader(buffer), Instructions);
        }
    }

    // Where a recorded dispatch goes; nothing else in a delegate function is unreachable.
    internal static readonly WasmInstruction DispatchMarker = WasmInstruction.NoOperand(WasmOpcodes.Unreachable);

    // Writes the dispatch marker where the dispatch goes, and keeps each request.
    internal sealed class RecordingDispatch : IDelegateDispatchEmitter
    {
        public List<(DelegateDispatch Dispatch, IFunctionIndexResolver FunctionIndices)> Requests { get; } = [];

        public void Emit(IWasmInstructionWriter code, DelegateDispatch dispatch, IFunctionIndexResolver functionIndices)
        {
            Requests.Add((dispatch, functionIndices));
            code.Write(DispatchMarker);
        }
    }

    // Writes a drop where the root frame goes, and keeps each frame local.
    internal sealed class RecordingRoots : IDelegateArgumentRootEmitter
    {
        public List<(MethodInstanceModel Invoke, int FrameLocal)> Requests { get; } = [];

        public void Emit(IWasmInstructionWriter code, MethodInstanceModel invoke, int frameLocal)
        {
            Requests.Add((invoke, frameLocal));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
        }
    }
}
