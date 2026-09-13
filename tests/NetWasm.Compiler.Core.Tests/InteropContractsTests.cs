using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

public sealed class InteropContractsTests
{
    private static readonly AssemblyIdentity Assembly = new("InteropTests");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity ResultType =
        CliTypeIdentity.FromStackKind(CliValueKind.I4);

    [Fact]
    public void JavaScriptInteropContractsExposeEveryDeclarationMember()
    {
        var import = new InteropImportDeclaration("invoke", "host")
        {
            IsPromise = true,
        };
        var export = new InteropExportDeclaration("published");
        var task = CliTypeIdentity.Named(
            Assembly,
            "System.Threading.Tasks",
            "Task",
            isValueType: false);
        var returnValue = new JavaScriptAsyncReturn(
            JavaScriptAsyncReturnKind.Task,
            ResultType);
        var method = CreateMethodInstance(0x06000001);
        var field = CreateFieldInstance(0x04000001);
        var binding = new JavaScriptAsyncMethodBinding(
            method.Definition.Key,
            returnValue,
            task,
            method,
            method,
            method)
        {
            ValueTaskTaskField = field,
            ValueTaskAsTask = method,
            StatusField = field,
            ResultField = field,
        };
        var witImport = new WitImportDeclaration("example:api", "invoke");
        var witExport = new WitExportDeclaration("example:api", "publish");
        var postReturn = new WitPostReturnDeclaration("example:api", "finish");

        Assert.Equal("invoke", import.FunctionName);
        Assert.Equal("host", import.ModuleName);
        Assert.True(import.IsPromise);
        Assert.Equal("published", export.ExportName);
        Assert.Equal(JavaScriptAsyncReturnKind.Task, returnValue.Kind);
        Assert.Equal(ResultType, returnValue.ResultType);
        Assert.True(returnValue.IsAsync);
        Assert.True(returnValue.HasResult);
        Assert.Equal(method.Definition.Key, binding.Method);
        Assert.Equal(returnValue, binding.Return);
        Assert.Equal(task, binding.TaskType);
        Assert.Same(method, binding.SetResult);
        Assert.Same(method, binding.SetException);
        Assert.Same(method, binding.SetCanceled);
        Assert.Same(field, binding.ValueTaskTaskField);
        Assert.Same(method, binding.ValueTaskAsTask);
        Assert.Same(field, binding.StatusField);
        Assert.Same(field, binding.ResultField);
        Assert.Equal("example:api", witImport.InterfaceName);
        Assert.Equal("invoke", witImport.FunctionName);
        Assert.Equal("example:api", witExport.InterfaceName);
        Assert.Equal("publish", witExport.FunctionName);
        Assert.Equal("example:api", postReturn.InterfaceName);
        Assert.Equal("finish", postReturn.FunctionName);
    }

    [Fact]
    public void AsyncAbiNamesAreStableForEveryDirection()
    {
        var method = new EntityKey(Assembly, 0x06000002);
        const string prefix = "InteropTests";

        Assert.Equal(
            $"netwasm.async.import.resolve.{prefix}.06000002",
            JavaScriptAsyncAbiNames.Resolve(method));
        Assert.Equal(
            $"netwasm.async.import.reject.{prefix}.06000002",
            JavaScriptAsyncAbiNames.Reject(method));
        Assert.Equal(
            $"netwasm.async.import.cancel.{prefix}.06000002",
            JavaScriptAsyncAbiNames.Cancel(method));
        Assert.Equal(
            $"netwasm.async.export.status.{prefix}.06000002",
            JavaScriptAsyncAbiNames.ExportStatus(method));
        Assert.Equal(
            $"netwasm.async.export.result.{prefix}.06000002",
            JavaScriptAsyncAbiNames.ExportResult(method));
        Assert.Equal(
            $"netwasm.async.export.complete.{prefix}.06000002",
            JavaScriptAsyncAbiNames.ExportComplete(method));
    }

    [Fact]
    public void JavaScriptAsyncSignatureClassifiesTaskShapesAndPlainTypes()
    {
        var task = CliTypeIdentity.Named(
            Assembly,
            "System.Threading.Tasks",
            "Task",
            isValueType: false);
        var valueTask = CliTypeIdentity.Named(
            Assembly,
            "System.Threading.Tasks",
            "ValueTask",
            isValueType: true);
        var genericTask = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly,
                "System.Threading.Tasks",
                "Task`1",
                isValueType: false),
            [ResultType]);
        var genericValueTask = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly,
                "System.Threading.Tasks",
                "ValueTask`1",
                isValueType: true),
            [ResultType]);
        var ordinary = CliTypeIdentity.Named(
            Assembly,
            "Example",
            "Result",
            isValueType: false);
        var genericOrdinary = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly,
                "Example",
                "Result`1",
                isValueType: false),
            [ResultType]);

        Assert.Equal(JavaScriptAsyncReturnKind.Task,
            JavaScriptAsyncSignature.Classify(task).Kind);
        Assert.Equal(JavaScriptAsyncReturnKind.ValueTask,
            JavaScriptAsyncSignature.Classify(valueTask).Kind);
        Assert.Equal(ResultType,
            JavaScriptAsyncSignature.Classify(genericTask).ResultType);
        Assert.Equal(ResultType,
            JavaScriptAsyncSignature.Classify(genericValueTask).ResultType);
        Assert.False(JavaScriptAsyncSignature.Classify(ordinary).IsAsync);
        Assert.False(JavaScriptAsyncSignature.Classify(genericOrdinary).IsAsync);
    }

    [Fact]
    public void CanonicalAbiContractsExposeEveryShapeAndFunctionMember()
    {
        var fieldType = new CanonicalAbiType(
            CanonicalAbiTypeKind.S32,
            ResultType);
        var type = new CanonicalAbiType(CanonicalAbiTypeKind.Result, ResultType)
        {
            Fields =
            [
                new CanonicalAbiField("value", fieldType),
            ],
            Cases =
            [
                new CanonicalAbiCase("ok", ResultTypeContract()),
                new CanonicalAbiCase("error", null),
            ],
            ElementType = ResultTypeContract(),
            SuccessType = ResultTypeContract(),
            ErrorType = ResultTypeContract(),
            ResourceTypeId = 7,
            FlagsCount = 3,
        };
        var parameter = new CanonicalAbiParameter("input", type);
        var method = new EntityKey(Assembly, 0x06000003);
        var postReturn = new EntityKey(Assembly, 0x06000004);
        var function = new CanonicalAbiFunction(
            "example:api",
            "run",
            method,
            [parameter],
            type)
        {
            PostReturnMethod = postReturn,
            Kind = CanonicalAbiFunctionKind.ExportedResourceDrop,
            ResourceName = "resource",
        };

        Assert.Equal(CanonicalAbiTypeKind.Result, type.Kind);
        Assert.Equal(ResultType, type.ManagedType);
        Assert.Single(type.Fields);
        Assert.Equal("value", type.Fields[0].Name);
        Assert.Same(fieldType, type.Fields[0].Type);
        Assert.Equal(2, type.Cases.Length);
        Assert.Equal("ok", type.Cases[0].Name);
        Assert.Equal("error", type.Cases[1].Name);
        Assert.NotNull(type.ElementType);
        Assert.NotNull(type.SuccessType);
        Assert.NotNull(type.ErrorType);
        Assert.Equal(7, type.ResourceTypeId);
        Assert.Equal(3, type.FlagsCount);
        Assert.Equal("input", parameter.Name);
        Assert.Same(type, parameter.Type);
        Assert.Equal("example:api", function.InterfaceName);
        Assert.Equal("run", function.FunctionName);
        Assert.Equal(method, function.ManagedMethod);
        Assert.Single(function.Parameters);
        Assert.Same(parameter, function.Parameters[0]);
        Assert.Same(type, function.Result);
        Assert.Equal(postReturn, function.PostReturnMethod);
        Assert.Equal(
            CanonicalAbiFunctionKind.ExportedResourceDrop,
            function.Kind);
        Assert.Equal("resource", function.ResourceName);
    }

    [Fact]
    public void ComponentAndHostContractsExposeEveryBoundaryMember()
    {
        var type = ResultTypeContract();
        var method = new EntityKey(Assembly, 0x06000005);
        var function = new CanonicalAbiFunction(
            "example:api",
            "run",
            method,
            [],
            type);
        var boundary = new ComponentBoundaryContract(
            "example:package",
            "world",
            [function],
            [function]);
        var status = new HostInteropStatusAbi(0, 1, 2);
        var layout = new HostInteropTargetLayout(4, 8, 12, 16, 20);
        var import = new HostInteropImport(
            "host",
            "invoke",
            ["i32", "string"],
            "i32")
        {
            AsyncReturn = "task",
            ResolveExport = "resolve",
            RejectExport = "reject",
            CancelExport = "cancel",
        };
        var export = new HostInteropExport(
            "publish",
            ["i32"],
            "string")
        {
            AsyncReturn = "value-task",
            StatusExport = "status",
            ResultExport = "result",
            CompleteExport = "complete",
        };
        var callback = new HostInteropCallback(
            "host",
            "invoke",
            1,
            "callback",
            ["i32"],
            "unit");
        var manifest = new HostInteropManifest(
            2,
            "wasm64",
            status,
            layout,
            [import],
            [export])
        {
            Callbacks = [callback],
        };
        var declaration = new HostCallbackDeclaration(
            method,
            0,
            CreateMethodInstance(0x06000006),
            "callback");

        Assert.Equal("example:package", boundary.Package);
        Assert.Equal("world", boundary.World);
        Assert.Single(boundary.Imports);
        Assert.Single(boundary.Exports);
        Assert.False(boundary.IsEmpty);
        Assert.True(ComponentBoundaryContract.Empty.IsEmpty);
        Assert.Equal(2, manifest.Version);
        Assert.Equal("wasm64", manifest.Target);
        Assert.Same(status, manifest.StatusAbi);
        Assert.Same(layout, manifest.TargetLayout);
        Assert.Same(import, manifest.Imports[0]);
        Assert.Same(export, manifest.Exports[0]);
        Assert.Same(callback, manifest.Callbacks[0]);
        Assert.Equal(0, status.SuccessStatus);
        Assert.Equal(1, status.HostFailureStatus);
        Assert.Equal(2, status.ScalarResultOffset);
        Assert.Equal(4, layout.ManagedReferenceSize);
        Assert.Equal(8, layout.StringLengthOffset);
        Assert.Equal(12, layout.StringDataOffset);
        Assert.Equal(16, layout.ArrayLengthOffset);
        Assert.Equal(20, layout.ArrayDataPointerOffset);
        Assert.Equal("host", import.Module);
        Assert.Equal("invoke", import.Name);
        Assert.True(import.Parameters.SequenceEqual(["i32", "string"]));
        Assert.Equal("i32", import.Result);
        Assert.Equal("task", import.AsyncReturn);
        Assert.Equal("resolve", import.ResolveExport);
        Assert.Equal("reject", import.RejectExport);
        Assert.Equal("cancel", import.CancelExport);
        Assert.Equal("publish", export.Name);
        Assert.True(export.Parameters.SequenceEqual(["i32"]));
        Assert.Equal("string", export.Result);
        Assert.Equal("value-task", export.AsyncReturn);
        Assert.Equal("status", export.StatusExport);
        Assert.Equal("result", export.ResultExport);
        Assert.Equal("complete", export.CompleteExport);
        Assert.Equal("host", callback.Module);
        Assert.Equal("invoke", callback.ImportName);
        Assert.Equal(1, callback.ParameterIndex);
        Assert.Equal("callback", callback.ExportName);
        Assert.True(callback.Parameters.SequenceEqual(["i32"]));
        Assert.Equal("unit", callback.Result);
        Assert.Equal(method, declaration.ImportMethod);
        Assert.Equal(0, declaration.ParameterIndex);
        Assert.Equal("callback", declaration.ExportName);
        Assert.Equal(0x06000006, declaration.Invoke.Definition.Key.MetadataToken);
    }

    private static CanonicalAbiType ResultTypeContract() =>
        new(CanonicalAbiTypeKind.S32, ResultType);

    private static MethodInstanceModel CreateMethodInstance(int token)
    {
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, token),
            TypeKey,
            "Invoke",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var type = CliTypeIdentity.Named(
            Assembly,
            "Interop",
            "Callbacks",
            isValueType: false);
        return new MethodInstanceModel(definition, type, [], definition.Signature);
    }

    private static FieldInstanceModel CreateFieldInstance(int token)
    {
        var definition = new FieldDefinitionModel(
            new EntityKey(Assembly, token),
            TypeKey,
            "Status",
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            true);
        var type = CliTypeIdentity.Named(
            Assembly,
            "Interop",
            "Callbacks",
            isValueType: false);
        return new FieldInstanceModel(definition, type, definition.SignatureType);
    }
}
