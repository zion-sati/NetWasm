using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

public sealed class ManagedWasmEmitterTests
{
    [Fact]
    public void LoweringObtainsObjectFieldStaticAndStringPlacementFromLayoutProvider()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var intrinsics = new FakeIntrinsics();
        var entry = program.GetMethod(EntryKey);
        Dictionary<EntityKey, StructuredMethod> methods = new()
        {
            [EntryKey] = Structure(
                program,
                entry,
                I(0, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
                I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                I(2, CilOperation.StoreField, new CilOperand.Entity(InstanceFieldKey)),
                I(3, CilOperation.LoadStaticField, new CilOperand.Entity(StaticFieldKey)),
                I(4, CilOperation.Return)),
            [ConstructorKey] = Structure(
                program,
                program.GetMethod(ConstructorKey),
                I(0, CilOperation.Return)),
            [StringMethodKey] = Structure(
                program,
                program.GetMethod(StringMethodKey),
                I(0, CilOperation.LoadString, new CilOperand.UserString("x")),
                I(1, CilOperation.Call, new CilOperand.Entity(StringLengthKey)),
                I(2, CilOperation.Return)),
        };
        RootSource temporary = new(RootSourceKind.AllocationTemporary, 0);
        var rootMaps = EmptyRootMaps(methods.Keys)
            .SetItem(
                EntryKey,
                new MethodRootMap(
                    EntryKey,
                    new Dictionary<RootSource, int> { [temporary] = 0 }
                        .ToImmutableDictionary(),
                    new Dictionary<int, SafepointRootMap>
                    {
                        [0] = new(0, [], [temporary]),
                    }.ToImmutableDictionary()));

        var modules = new WasmModuleEmitterFactory();
        var emitter = CreateManagedWasmEmitter(program, intrinsics, layouts, modules);
        var stringMethod = program.GetMethod(StringMethodKey);
        var stringLength = program.GetMethod(StringLengthKey);
        var identities = CreateTypeIdentities(program);
        var caller = new ManagedMethodIdentity(new MethodInstanceModel(
            stringMethod,
            identities.Resolve(stringMethod.DeclaringType),
            [],
            stringMethod.Signature).CanonicalName);
        var target = new MethodInstanceModel(
            stringLength,
            identities.Resolve(stringLength.DeclaringType),
            [],
            stringLength.Signature);
        var callSiteKey = new ManagedCallSiteKey(caller, 1);
        var request = WasmEmissionRequest.Create(
            entry,
            methods,
            rootMaps,
            [],
            ImmutableDictionary<string, EntityKey>.Empty) with
        {
            ManagedCallSites = ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty
                .Add(callSiteKey, new ManagedCallSite(
                    callSiteKey,
                    ManagedCallOperation.Direct,
                    new ManagedMethodIdentity(target.CanonicalName),
                    target,
                    ConstrainedType: null)),
        };
        var module = emitter.Emit(request).Module;
        var repeatedModule = emitter.Emit(request).Module;

        Assert.Equal(TypeKey, layouts.ObjectRequest);
        Assert.Equal(InstanceFieldKey, layouts.FieldRequest);
        Assert.Equal(StaticFieldKey, layouts.StaticFieldRequest);
        Assert.Equal("x", layouts.StringRequest);
        byte[] fieldStore = [0x36, 0x02, 0x34];
        byte[] stringLengthLoad = [0x28, 0x02, 0x2c];
        Assert.True(module.AsSpan().IndexOf(fieldStore) >= 0);
        Assert.True(module.AsSpan().IndexOf(stringLengthLoad) >= 0);
        Assert.True(module.AsSpan().IndexOf("register_type"u8) >= 0);
        Assert.True(module.AsSpan().IndexOf("register_static_root"u8) >= 0);
        Assert.True(module.AsSpan().IndexOf("root_frame_enter"u8) >= 0);
        Assert.True(module.AsSpan().IndexOf("root_frame_leave"u8) >= 0);
        Assert.Equal(module, repeatedModule);
    }

    [Fact]
    public void EmitterValidatesConstructorAndEmitArguments()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var intrinsics = new FakeIntrinsics();
        Assert.Throws<ArgumentNullException>(
            () => CreateManagedWasmEmitter(
                null!, intrinsics, layouts, new WasmModuleEmitterFactory()));
        Assert.Throws<ArgumentNullException>(
            () => CreateManagedWasmEmitter(
                program, null!, layouts, new WasmModuleEmitterFactory()));
        Assert.Throws<ArgumentNullException>(
            () => CreateManagedWasmEmitter(
                program,
                intrinsics,
                (RecordingLayoutProvider)null!,
                new WasmModuleEmitterFactory()));

        Assert.Throws<ArgumentNullException>(
            () => CreateManagedWasmEmitter(program, intrinsics, layouts, null!));

        var emitter = CreateManagedWasmEmitter(
            program, intrinsics, layouts, new WasmModuleEmitterFactory());
        var entry = program.GetMethod(EntryKey);
        var methods = new Dictionary<EntityKey, StructuredMethod>();
        var rootMaps = new Dictionary<EntityKey, MethodRootMap>();
        Assert.Throws<ArgumentNullException>(
            () => emitter.Emit(null!, methods, rootMaps, [], new Dictionary<string, EntityKey>()));
        Assert.Throws<ArgumentNullException>(
            () => emitter.Emit(entry, null!, rootMaps, [], new Dictionary<string, EntityKey>()));
        Assert.Throws<ArgumentNullException>(
            () => emitter.Emit(entry, methods, null!, [], new Dictionary<string, EntityKey>()));
        Assert.Throws<ArgumentNullException>(
            () => emitter.Emit(entry, methods, rootMaps, null!, new Dictionary<string, EntityKey>()));
        Assert.Throws<ArgumentNullException>(
            () => emitter.Emit(entry, methods, rootMaps, [], null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit((WasmEmissionRequest)null!));
    }

    [Fact]
    public void EveryConstructorCapabilityIsRequiredIndependently()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        object[] arguments =
        [
            program,
            program,
            program,
            program,
            program,
            program,
            new FakeIntrinsics(),
            layouts,
            layouts,
            layouts,
            layouts,
            layouts,
            layouts,
            layouts,
            layouts,
            layouts,
            new WasmModuleEmitterFactory(),
        ];
        var constructor = Assert.Single(typeof(ManagedWasmEmitter).GetConstructors());
        var parameters = constructor.GetParameters();

        for (var index = 0; index < arguments.Length; index++)
        {
            var invalid = arguments.ToArray();
            invalid[index] = null!;

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
                () => constructor.Invoke(invalid));
            var argument = Assert.IsType<ArgumentNullException>(exception.InnerException);
            Assert.Equal(parameters[index].Name, argument.ParamName);
        }
    }

}
