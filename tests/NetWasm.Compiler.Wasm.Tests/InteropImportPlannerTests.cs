using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class InteropImportPlannerTests
{
    [Fact]
    public void StringImportResultPlansCopyAndHandleImportsInStableOrder()
    {
        var request = RequestWithImport(CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false));

        var plan = new InteropImportPlanner().Build(request, firstIndex: 27);

        Assert.Equal(
            [
                RuntimeAbi.HostInteropStringLength,
                RuntimeAbi.HostInteropCopyStringUtf16,
                RuntimeAbi.HostInteropReleaseHandle,
            ],
            plan.Imports.Select(import => import.Name));
        Assert.Equal(27, plan.StringLength.Value);
        Assert.Equal(28, plan.CopyStringUtf16.Value);
        Assert.Equal(29, plan.ReleaseHandle.Value);
        Assert.False(plan.ByteLength.IsPresent);
        Assert.False(plan.ReleaseSubscription.IsPresent);
    }

    [Fact]
    public void ByteAndSubscriptionResultsPlanDistinctLifetimeImports()
    {
        var bytes = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var subscription = CliTypeIdentity.Named(
            Assembly,
            "System.Runtime.InteropServices.JavaScript",
            "JSSubscription",
            isValueType: false);
        var byteRequest = RequestWithImport(bytes);
        var subscriptionRequest = RequestWithImport(subscription);

        var bytePlan = new InteropImportPlanner().Build(byteRequest, firstIndex: 40);
        var subscriptionPlan = new InteropImportPlanner().Build(
            subscriptionRequest,
            firstIndex: 50);

        Assert.Equal(40, bytePlan.ByteLength.Value);
        Assert.Equal(41, bytePlan.CopyBytes.Value);
        Assert.Equal(42, bytePlan.ReleaseHandle.Value);
        Assert.Equal(50, subscriptionPlan.ReleaseHandle.Value);
        Assert.Equal(51, subscriptionPlan.ReleaseSubscription.Value);
    }

    [Fact]
    public void JavaScriptStringResultExportPlansHostHandleImports()
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliTypeIdentity.Primitive(
                "string",
                CliValueKind.ManagedReference,
                isValueType: false)),
        };
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(
                Assembly,
                "Tests",
                "EntryPoint",
                isValueType: false),
            [],
            definition.Signature);
        var request = WasmEmissionRequest.Create(
            definition,
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
            [],
            ImmutableDictionary<string, EntityKey>.Empty.Add("value", definition.Key),
            methodInstances: ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                instance.CanonicalName,
                instance)) with
        {
            UseJavaScriptExportBoundary = true,
        };

        var plan = new InteropImportPlanner().Build(request, firstIndex: 17);

        Assert.True(plan.UseJavaScriptExportBoundary);
        Assert.Equal(17, plan.StringLength.Value);
        Assert.Equal(18, plan.CopyStringUtf16.Value);
        Assert.Equal(19, plan.ReleaseHandle.Value);
    }

    [Fact]
    public void DefaultCoreApplicationStringExportUsesJavaScriptBoundaryImports()
    {
        var program = new FakeProgram();
        var stringType = CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false);
        var definition = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                stringType),
        };
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(
                Assembly,
                "Tests",
                "EntryPoint",
                isValueType: false),
            [],
            definition.Signature);
        var request = WasmEmissionRequest.Create(
            definition,
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
            [],
            ImmutableDictionary<string, EntityKey>.Empty.Add("accept", definition.Key),
            methodInstances: ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                instance.CanonicalName,
                instance));

        var plan = new InteropImportPlanner().Build(request, firstIndex: 23);

        Assert.True(plan.UseJavaScriptExportBoundary);
        Assert.Equal(23, plan.StringLength.Value);
        Assert.Equal(24, plan.CopyStringUtf16.Value);
        Assert.Equal(25, plan.ReleaseHandle.Value);
    }

    private static WasmEmissionRequest RequestWithImport(CliTypeIdentity returnType)
    {
        var program = new FakeProgram();
        var entry = program.GetMethod(EntryKey);
        var import = entry with
        {
            Key = Key(0x06000030),
            Name = "Imported",
            Signature = MethodSignatureModel.Create(returnType),
            RelativeVirtualAddress = 0,
            JSImport = new("imported", null),
        };
        return WasmEmissionRequest.Create(
            entry,
            ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
            [],
            ImmutableDictionary<string, EntityKey>.Empty,
            jsImportMethods: [import]);
    }
}
