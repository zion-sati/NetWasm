using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StaticInitializationEmitterTests
{
    private static readonly WasmTarget[] Targets = [WasmTarget.Wasm32, WasmTarget.Wasm64];
    private static readonly bool[] BooleanValues = [false, true];

    public static IEnumerable<object[]> InitializationTriggers =>
        from target in Targets
        from constructed in BooleanValues
        from beforeFieldInit in BooleanValues
        from methodCall in BooleanValues
        select new object[] { target, constructed, beforeFieldInit, methodCall };

    [Theory]
    [MemberData(nameof(InitializationTriggers))]
    public void BeforeFieldInitGuardsTriggerOnlyForFieldAccessEvenWhenAlreadyPlanned(
        WasmTarget target, bool constructed, bool beforeFieldInit, bool methodCall)
    {
        var program = new InitializerProgram(true, beforeFieldInit);
        var owner = CliTypeIdentity.Named(Assembly, "Test", "Owner", false);
        if (constructed) owner = CliTypeIdentity.GenericInstantiation(owner, [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var key = constructed ? $"{owner.CanonicalName}::0x{EntryKey.MetadataToken:x8}" : StaticInitializerGuard.KeyFor(EntryKey);
        var data = ModuleDataPlan.Empty with
        {
            StaticInitializerGuards = ImmutableDictionary<string, StaticInitializerGuard>.Empty.Add(key,
                new(240, constructed ? null : EntryKey, constructed ? key : null)
                { FunctionIndex = OptionalFunctionIndex.At(42) }),
        };
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IStaticInitializationEmitter>(new StaticInitializationEmitter(program, program,
            new AddressInstructionEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)))));

        emitter.Emit(new(TypeKey, owner, data, methodCall), writer, new RecordingIndices());

        Assert.Equal(!(beforeFieldInit && methodCall), writer.ToInstructions().Any(item => item.Opcode == WasmOpcodes.Call));
        if (beforeFieldInit && methodCall) Assert.Empty(writer.ToInstructions());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void SuccessfulGuardSkipsTheSharedInitializationBoundary(WasmTarget target, bool constructed)
    {
        var program = new InitializerProgram(true);
        var owner = CliTypeIdentity.Named(Assembly, "Test", "Owner", false);
        if (constructed) owner = CliTypeIdentity.GenericInstantiation(owner, [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var key = constructed ? $"{owner.CanonicalName}::0x{EntryKey.MetadataToken:x8}" : StaticInitializerGuard.KeyFor(EntryKey);
        var data = ModuleDataPlan.Empty with
        {
            StaticInitializerGuards = ImmutableDictionary<string, StaticInitializerGuard>.Empty.Add(key,
                new(240, constructed ? null : EntryKey, constructed ? key : null)
                { FunctionIndex = OptionalFunctionIndex.At(42) }),
        };
        var indices = new RecordingIndices();
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IStaticInitializationEmitter>(new StaticInitializationEmitter(program, program,
            new AddressInstructionEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)))));

        emitter.Emit(new(TypeKey, owner, data), writer, indices);

        var instructions = writer.ToInstructions();
        Assert.Equal(new byte[]
        {
            target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            WasmOpcodes.I32Load, WasmOpcodes.I32Constant, WasmOpcodes.I32Equal,
            WasmOpcodes.I32EqualZero, WasmOpcodes.If, WasmOpcodes.Call, WasmOpcodes.End,
        }, instructions.Select(item => item.Opcode));
        Assert.Equal(2, instructions[2].Operand.SignedValue);
        Assert.Equal(42U, instructions[6].Operand.UnsignedValue);
        Assert.Null(indices.Constructed);
        Assert.Null(indices.Direct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnersWithoutAPlannedInitializerDoNotEmitAGuard(bool hasInitializer)
    {
        var program = new InitializerProgram(hasInitializer);
        var indices = new RecordingIndices();
        var writer = new RecordingInstructionWriter();
        var emitter = Assert.IsAssignableFrom<IStaticInitializationEmitter>(new StaticInitializationEmitter(program, program,
            new AddressInstructionEmitter(new RecordingLayoutProvider())));

        emitter.Emit(new(TypeKey, null, ModuleDataPlan.Empty), writer, indices);

        Assert.Empty(writer.ToInstructions());
        Assert.Null(indices.Direct);
        Assert.Null(indices.Constructed);
    }

    private sealed class InitializerProgram(bool hasInitializer, bool beforeFieldInit = false) : ITypeRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            new(key, "Test", "Owner", false, [], hasInitializer ? [EntryKey, ConstructorKey] : [ConstructorKey])
            { IsBeforeFieldInit = beforeFieldInit };

        public MethodDefinitionModel GetMethod(EntityKey key) => new(key, TypeKey,
            key == EntryKey ? ".cctor" : "Method", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
    }

    private sealed class RecordingIndices : IFunctionIndexResolver
    {
        public EntityKey? Direct { get; private set; }
        public string? Constructed { get; private set; }
        public int Resolve(EntityKey method) { Direct = method; return 42; }
        public int Resolve(string method) { Constructed = method; return 42; }
        public int Resolve(MethodInstanceModel method) => throw new InvalidOperationException();
    }
}
