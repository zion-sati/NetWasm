using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

public sealed class RootPublicationIntegrationTests
{
    [Fact]
    public void LoweringWritesEveryGeneratedRootSourceKindAndRejectsUnknownKinds()
    {
        var program = new FakeProgram();
        var emitter = CreateManagedWasmEmitter(
            program,
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            new WasmModuleEmitterFactory());
        var constructor = program.GetMethod(ConstructorKey);
        var structured = StructureWithLocals(
            program,
            constructor,
            [CliValueKind.ManagedReference],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(4, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(6, CilOperation.Return));
        RootSource[] sources =
        [
            new(RootSourceKind.Argument, 0),
            new(RootSourceKind.Local, 0),
            new(RootSourceKind.EvaluationStack, 0),
            new(RootSourceKind.AllocationTemporary, 0),
        ];
        MethodRootMap rootMap = new(
            ConstructorKey,
            sources.Select((source, slot) => (source, slot))
                .ToImmutableDictionary(item => item.source, item => item.slot),
            new Dictionary<int, SafepointRootMap>
            {
                [3] = new(3, [.. sources[..3]], [.. sources]),
            }.ToImmutableDictionary());

        var module = emitter.Emit(
            constructor,
            new Dictionary<EntityKey, StructuredMethod> { [ConstructorKey] = structured },
            new Dictionary<EntityKey, MethodRootMap> { [ConstructorKey] = rootMap },
            [],
            ImmutableDictionary<string, EntityKey>.Empty);

        Assert.NotEmpty(module);

        RootSource unknown = new((RootSourceKind)99, 0);
        MethodRootMap invalidMap = new(
            ConstructorKey,
            new Dictionary<RootSource, int> { [unknown] = 0 }.ToImmutableDictionary(),
            new Dictionary<int, SafepointRootMap>
            {
                [3] = new(3, [unknown], [unknown]),
            }.ToImmutableDictionary());
        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            constructor,
            new Dictionary<EntityKey, StructuredMethod> { [ConstructorKey] = structured },
            new Dictionary<EntityKey, MethodRootMap> { [ConstructorKey] = invalidMap },
            [],
            ImmutableDictionary<string, EntityKey>.Empty));
    }

}
