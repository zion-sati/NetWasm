using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Interop;

public interface IHostInteropManifestBuilder
{
    HostInteropManifest Build(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        ManagedLayoutSnapshot layouts,
        ImmutableArray<string> importModules);
}

internal sealed class HostInteropManifestBuilder : IHostInteropManifestBuilder
{
    public HostInteropManifest Build(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        ManagedLayoutSnapshot layouts,
        ImmutableArray<string> importModules)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(layouts);
        if (importModules.IsDefault || importModules.Any(module =>
                string.IsNullOrWhiteSpace(module) || module.Contains('\0') ||
                NativeDeclarationValidator.IsReservedImportModule(module)))
        {
            throw new CompilerException(new(DiagnosticCode.NativeInterop,
                "An import module must be named and cannot be a NetWasm or WASI import module."));
        }
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
            .Select(pair => BuildExport(pair.Export.Name, pair.Method, pair.Method.JSExport!, layouts))
            .OrderBy(export => export.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        var callbacks = program.HostCallbacks.Select(callback =>
            {
                var import = program.JSImportMethods.Single(method => method.Key == callback.ImportMethod).JSImport!;
                // A Function<...> marker lists the callback's values, its result last.
                var values = JavaScriptMarshalAsRules.Arguments(import.MarshalAs.Parameter(callback.ParameterIndex));
                var invoke = callback.Invoke.Signature;
                return new HostInteropCallback(
                    import.ModuleName ?? RuntimeAbi.HostModule,
                    import.FunctionName,
                    callback.ParameterIndex,
                    callback.ExportName,
                    [.. invoke.ParameterSignatureTypes.Select((type, index) =>
                        ToAbiType(type, layouts, Marker(values, index)))],
                    ToAbiType(invoke.ReturnSignatureType, layouts, Marker(values, invoke.ParameterSignatureTypes.Length)));
            })
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
                layouts.ArrayDataPointerOffset,
                ManagedLayoutSnapshot.SpanReferenceOffset,
                layouts.SpanLengthOffset),
            imports,
            managedExports)
        {
            Callbacks = callbacks,
            WitImports = witImports,
            ImportModules = [.. importModules.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
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
                    callback.ParameterIndex == index)
                    ? "callback"
                    : ToAbiType(type, layouts, method.JSImport.MarshalAs.Parameter(index)))],
            method.JSImport.IsPromise
                ? "promise"
                : ToAsyncAbiType(asyncReturn, method.Signature.ReturnSignatureType, layouts, method.JSImport.MarshalAs.Result))
        {
            Call = JavaScriptDirectImport.Applies(method) ? JavaScriptDirectImport.DirectCall : JavaScriptDirectImport.StatusCall,
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
        InteropExportDeclaration declaration,
        ManagedLayoutSnapshot layouts)
    {
        var asyncReturn = JavaScriptAsyncSignature.Classify(
            method.Signature.ReturnSignatureType);
        return new HostInteropExport(
            name,
            [.. method.Signature.ParameterSignatureTypes.Select((type, index) =>
                ToAbiType(type, layouts, declaration.MarshalAs.Parameter(index)))],
            ToAsyncAbiType(asyncReturn, method.Signature.ReturnSignatureType, layouts, declaration.MarshalAs.Result))
        {
            ParameterNames = declaration.ParameterNames,
            NonNullableParameters = declaration.NonNullableParameters,
            NonNullableResult = declaration.NonNullableResult,
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
            CompletionResult = asyncReturn.IsAsync ? "exception-handle-v1" : null,
        };
    }

    // A Promise<...> marker names the settled value's JavaScript type.
    private static string ToAsyncAbiType(
        JavaScriptAsyncReturn asyncReturn,
        CliTypeIdentity declaredReturn,
        ITargetLayout layouts,
        string? marker) => asyncReturn.IsAsync
            ? asyncReturn.ResultType is null
                ? "void"
                : ToAbiType(asyncReturn.ResultType, layouts, Marker(JavaScriptMarshalAsRules.Arguments(marker), 0))
            : ToAbiType(declaredReturn, layouts, marker);

    // A long a marker makes a Number crosses as Int52.
    private static string ToAbiType(CliTypeIdentity type, ITargetLayout layouts, string? marker) =>
        JavaScriptMarshalAsRules.SelectsInt52(type, marker)
            ? JavaScriptMarshalAsRules.Int52 + (JavaScriptNullable.IsSupported(type) ? "?" : "")
            : ToAbiType(type, layouts);

    private static string? Marker(ImmutableArray<string> markers, int index) =>
        index < markers.Length ? markers[index] : null;

    private static string ToAsyncKind(JavaScriptAsyncReturn asyncReturn) =>
        asyncReturn.Kind == JavaScriptAsyncReturnKind.Task ? "task" : "value-task";

    private static string ToAbiType(CliTypeIdentity type, ITargetLayout layouts) => type.CanonicalName switch
    {
        _ when JavaScriptNullable.Underlying(type) is { } underlying => ToAbiType(underlying, layouts) + "?",
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
        _ when JavaScriptConversion.IsDate(type) => "date",
        _ when JavaScriptConversion.HandleAbiType(type) is { } handleValue => handleValue,
        _ when IsString(type) => "string",
        _ when JavaScriptValueArray.IsSupported(type) => JavaScriptValueArray.GetAbiType(type),
        _ when JavaScriptMemoryView.IsSupported(type) => JavaScriptMemoryView.GetAbiType(type),
        _ when IsJSObject(type) => "object",
        _ when IsJSSubscription(type) => "subscription",
        _ => throw new InvalidOperationException(
            $"unsupported host ABI type '{type.CanonicalName}'"),
    };

    private static bool IsString(CliTypeIdentity type) => type.CanonicalName == "primitive:string";

    private static bool IsJSObject(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";

    private static bool IsJSSubscription(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSSubscription";
}
