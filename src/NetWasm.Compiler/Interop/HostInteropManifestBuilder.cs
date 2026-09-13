using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Interop;

public interface IHostInteropManifestBuilder
{
    HostInteropManifest Build(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        ManagedLayoutSnapshot layouts);
}

internal sealed class HostInteropManifestBuilder : IHostInteropManifestBuilder
{
    public HostInteropManifest Build(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        ManagedLayoutSnapshot layouts)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(layouts);
        var imports = program.JSImportMethods
            .Select(method => BuildImport(program, method, layouts))
            .OrderBy(import => import.Module, StringComparer.Ordinal)
            .ThenBy(import => import.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        var managedExports = exports
            .Select(export => (
                Export: export,
                Method: program.Methods[export.Method].Method.Definition))
            .Where(pair => pair.Method.JSExport is not null)
            .Select(pair => BuildExport(pair.Export.Name, pair.Method, layouts))
            .OrderBy(export => export.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        var callbacks = program.HostCallbacks.Select(callback => new HostInteropCallback(
                program.JSImportMethods.Single(method => method.Key == callback.ImportMethod)
                    .JSImport!.ModuleName ?? RuntimeAbi.HostModule,
                program.JSImportMethods.Single(method => method.Key == callback.ImportMethod)
                    .JSImport!.FunctionName,
                callback.ParameterIndex,
                callback.ExportName,
                [.. callback.Invoke.Signature.ParameterSignatureTypes.Select(type => ToAbiType(type, layouts))],
                ToAbiType(callback.Invoke.Signature.ReturnSignatureType, layouts)))
            .OrderBy(callback => callback.ExportName, StringComparer.Ordinal)
            .ToImmutableArray();
        var witImports = program.WitImportMethods
            .Select(method => method.WitImport!.Identity)
            .Distinct()
            .OrderBy(identity => identity.InterfaceName, StringComparer.Ordinal)
            .ThenBy(identity => identity.FunctionName, StringComparer.Ordinal)
            .Select(identity => new HostInteropWitImport(
                identity.InterfaceName,
                identity.FunctionName))
            .ToImmutableArray();
        return new HostInteropManifest(
            RuntimeAbi.HostInteropAbiVersion,
            layouts.Target.Target == WasmTarget.Wasm32 ? "wasm32" : "wasm64",
            new HostInteropStatusAbi(0, 1, 0),
            new HostInteropTargetLayout(
                layouts.Target.ObjectReferenceSize,
                layouts.StringLengthOffset,
                layouts.StringDataOffset,
                layouts.ArrayLengthOffset,
                layouts.ArrayDataPointerOffset),
            imports,
            managedExports)
        {
            Callbacks = callbacks,
            WitImports = witImports,
        };
    }

    private static HostInteropImport BuildImport(
        ReachableProgram program,
        MethodDefinitionModel method,
        ManagedLayoutSnapshot layouts)
    {
        var asyncReturn = JavaScriptAsyncSignature.Classify(
            method.Signature.ReturnSignatureType);
        return new HostInteropImport(
            method.JSImport!.ModuleName ?? RuntimeAbi.HostModule,
            method.JSImport.FunctionName,
            [.. method.Signature.ParameterSignatureTypes.Select((type, index) =>
                program.HostCallbacks.Any(callback => callback.ImportMethod == method.Key &&
                    callback.ParameterIndex == index) ? "callback" : ToAbiType(type, layouts))],
            method.JSImport.IsPromise
                ? "promise"
                : ToAsyncAbiType(asyncReturn, method.Signature.ReturnSignatureType, layouts))
        {
            AsyncReturn = asyncReturn.IsAsync ? ToAsyncKind(asyncReturn) : null,
            ResolveExport = asyncReturn.IsAsync
                ? JavaScriptAsyncAbiNames.Resolve(method.Key)
                : null,
            RejectExport = asyncReturn.IsAsync
                ? JavaScriptAsyncAbiNames.Reject(method.Key)
                : null,
            CancelExport = asyncReturn.IsAsync
                ? JavaScriptAsyncAbiNames.Cancel(method.Key)
                : null,
        };
    }

    private static HostInteropExport BuildExport(
        string name,
        MethodDefinitionModel method,
        ManagedLayoutSnapshot layouts)
    {
        var asyncReturn = JavaScriptAsyncSignature.Classify(
            method.Signature.ReturnSignatureType);
        return new HostInteropExport(
            name,
            [.. method.Signature.ParameterSignatureTypes.Select(type => ToAbiType(type, layouts))],
            ToAsyncAbiType(asyncReturn, method.Signature.ReturnSignatureType, layouts))
        {
            AsyncReturn = asyncReturn.IsAsync ? ToAsyncKind(asyncReturn) : null,
            StatusExport = asyncReturn.IsAsync
                ? JavaScriptAsyncAbiNames.ExportStatus(method.Key)
                : null,
            ResultExport = asyncReturn.IsAsync && asyncReturn.HasResult
                ? JavaScriptAsyncAbiNames.ExportResult(method.Key)
                : null,
            CompleteExport = asyncReturn.IsAsync
                ? JavaScriptAsyncAbiNames.ExportComplete(method.Key)
                : null,
        };
    }

    private static string ToAsyncAbiType(
        JavaScriptAsyncReturn asyncReturn,
        CliTypeIdentity declaredReturn,
        ITargetLayout layouts) => asyncReturn.IsAsync
            ? asyncReturn.ResultType is null ? "void" : ToAbiType(asyncReturn.ResultType, layouts)
            : ToAbiType(declaredReturn, layouts);

    private static string ToAsyncKind(JavaScriptAsyncReturn asyncReturn) =>
        asyncReturn.Kind == JavaScriptAsyncReturnKind.Task ? "task" : "value-task";

    private static string ToAbiType(CliTypeIdentity type, ITargetLayout layouts) => type.CanonicalName switch
    {
        "primitive:void" => "void",
        "primitive:bool" => "bool",
        "primitive:i1" => "i8",
        "primitive:u1" => "u8",
        "primitive:char" => "char",
        "primitive:i2" => "i16",
        "primitive:u2" => "u16",
        "primitive:i4" => "i32",
        "primitive:u4" => "u32",
        "primitive:i8" => "i64",
        "primitive:u8" => "u64",
        "primitive:f4" => "f32",
        "primitive:f8" => "f64",
        "primitive:nativeint" => layouts.Target.UsesMemory64 ? "i64" : "i32",
        "primitive:nativeuint" => layouts.Target.UsesMemory64 ? "u64" : "u32",
        _ when IsString(type) => "string",
        _ when IsByteArray(type) => "bytes",
        _ when IsJSObject(type) => "object",
        _ when IsJSSubscription(type) => "subscription",
        _ => throw new InvalidOperationException(
            $"unsupported host ABI type '{type.CanonicalName}'"),
    };

    private static bool IsString(CliTypeIdentity type) => type.CanonicalName == "primitive:string";

    private static bool IsByteArray(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray &&
            type.ElementType!.CanonicalName == "primitive:u1";

    private static bool IsJSObject(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";

    private static bool IsJSSubscription(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSSubscription";
}
