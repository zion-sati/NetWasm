using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmModuleTargetFactoryTests
{
    [Fact]
    public void CreatesOneImmutableTargetFromTheEmissionRequest()
    {
        var program = new FakeProgram();
        var request = WithEntryPointCallable(CreateEmissionRequest(program));
        var byRefEffects = new ManagedByRefEffects(
            System.Collections.Immutable.ImmutableDictionary<string,
                ManagedByRefMethodEffects>.Empty.Add(
                    "method",
                    new([0], [], [])));
        request = request with { ManagedByRefEffects = byRefEffects };
        var layouts = new RecordingLayoutProvider();
        var factory = new WasmModuleTargetFactory(
            new WasmModulePlanner(
                program,
                program,
                program,
                new FakeIntrinsics(),
                new InteropImportPlanner(),
                WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator(),
                new NativeImportPlanner(NativeAbiTestSupport.ScalarPlanner()),
                NativeAbiTestSupport.CallbackPlanner(),
                new FinalizationPlanBuilder(layouts, new FakeIntrinsics())),
            new ModuleDataPlanner(
                layouts,
                layouts,
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()),
            new StaticInitializerFunctionPlanner(layouts, program),
            new JSImportForwarderPlanner(program));

        var target = factory.Create(request);

        Assert.Same(request, target.Request);
        Assert.Equal(target.Plan.FunctionIndices, target.Instructions.FunctionIndices);
        Assert.Equal(target.ModuleData, target.Instructions.ModuleData);
        Assert.Same(target.Plan.NativeImports, target.Instructions.NativeImports);
        Assert.True(target.Instructions.ManagedByRefEffects.TryGetValue("method", out var effects));
        Assert.Same(byRefEffects.Methods["method"], effects);
        // A method's emission looks up the module's plans through the target.
        Assert.All(target.ModuleData.ExceptionMetadata, pair =>
            Assert.Equal(pair.Value, target.Instructions.ExceptionMetadata.Required(pair.Key)));
        Assert.All(target.ModuleData.StaticInitializerGuards, pair =>
            Assert.Same(pair.Value, target.Instructions.StaticInitializerGuards.Required(pair.Key)));
        Assert.True(target.Instructions.RootMaps.TryGetValue(
            ManagedMethodBodyKey.Resolve(request.Methods.Values.Single()), out var rootMap));
        Assert.Same(request.RootMaps.Values.Single(), rootMap);
        Assert.Same(request.CallableMethods, target.Instructions.CallableMethods.Read());
        Assert.False(request.HasReenterableHostEntries);
        Assert.Null(target.ModuleData.StateRegistrationGuardAddress);
    }

    [Fact]
    public void IncludesConstructedMethodsInModuleDataOrdering()
    {
        var program = new FakeProgram();
        var request = WithEntryPointCallable(CreateEmissionRequest(program));
        var constructed = request.Methods.Values.Single();
        request = request with
        {
            ConstructedMethods = new Dictionary<string, StructuredMethod>
            {
                ["constructed"] = constructed,
            },
            ConstructedRootMaps = new Dictionary<string, MethodRootMap>
            {
                ["constructed"] = request.RootMaps.Values.Single(),
            },
        };
        var layouts = new RecordingLayoutProvider();
        var factory = new WasmModuleTargetFactory(
            new WasmModulePlanner(
                program,
                program,
                program,
                new FakeIntrinsics(),
                new InteropImportPlanner(),
                WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator(),
                new NativeImportPlanner(NativeAbiTestSupport.ScalarPlanner()),
                NativeAbiTestSupport.CallbackPlanner(),
                new FinalizationPlanBuilder(layouts, new FakeIntrinsics())),
            new ModuleDataPlanner(
                layouts,
                layouts,
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()),
            new StaticInitializerFunctionPlanner(layouts, program),
            new JSImportForwarderPlanner(program));

        var target = factory.Create(request);

        Assert.Equal(
            "constructed",
            Assert.Single(target.Plan.OrderedConstructedMethods).CanonicalName);
    }
    private static WasmEmissionRequest WithEntryPointCallable(WasmEmissionRequest request)
    {
        var method = request.EntryPoint!;
        var instance = new MethodInstanceModel(
            method,
            new TestCilTypeIdentityResolver().Resolve(method.DeclaringType),
            [],
            method.Signature);
        var identity = new ManagedMethodIdentityFactory().Create(instance);
        var callables = new Dictionary<string, MethodInstanceModel>(StringComparer.Ordinal);
        foreach (var callable in request.CallableMethods)
        {
            callables.Add(callable.Key, callable.Value);
        }

        callables[identity.CanonicalName] = instance;
        return request with { CallableMethods = callables };
    }
}
