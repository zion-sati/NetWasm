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
        var layouts = new RecordingLayoutProvider();
        var factory = new WasmModuleTargetFactory(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            new ModuleDataPlanner(
                layouts,
                layouts,
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()));

        var target = factory.Create(request);

        Assert.Same(request, target.Request);
        Assert.Equal(target.Plan.FunctionIndices, target.Instructions.FunctionIndices);
        Assert.Equal(target.ModuleData, target.Instructions.ModuleData);
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
        };
        var layouts = new RecordingLayoutProvider();
        var factory = new WasmModuleTargetFactory(
            new WasmModulePlanner(
                program,
                program,
                program,
                new InteropImportPlanner(),
                WasmRuntimeImports.CreateCatalog(),
            new DisabledStackTraceMethodPlanBuilder(),
                new WasmModulePlanInvariantValidator()),
            new ModuleDataPlanner(
                layouts,
                layouts,
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            new TestStructuredMethodEmissionPlanner(new ManagedMethodIdentityFactory(), new TestCilTypeIdentityResolver()));

        var target = factory.Create(request);

        Assert.Equal(
            "constructed",
            Assert.Single(target.Plan.OrderedConstructedMethods).CanonicalName);
    }
    private static WasmEmissionRequest WithEntryPointCallable(WasmEmissionRequest request)
    {
        var method = request.EntryPoint;
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
