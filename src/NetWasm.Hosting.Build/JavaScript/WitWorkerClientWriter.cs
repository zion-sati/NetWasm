using System.Collections.Immutable;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record WitWorkerClientRequest(
    ReadOnlyMemory<byte> ContractJson, string HostingJavaScriptRoot,
    string WorkerFileName, string BuildFingerprint, string ManifestSha256);

public sealed record WitWorkerClientSource(byte[] JavaScript, byte[] Declaration);

public interface IWitWorkerClientWriter
{
    WitWorkerClientSource Write(WitWorkerClientRequest request);
}

/// <summary>Writes the remote asynchronous API while keeping Jco's native value representations.</summary>
public sealed class WitWorkerClientWriter(
    IWitWorkerContractReader contracts,
    IWitWorkerValueLayoutPlanner layouts,
    ImmutableDictionary<string, IWitWorkerValueSourceWriter> values) : IWitWorkerClientWriter
{
    private readonly IWitWorkerContractReader _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly IWitWorkerValueLayoutPlanner _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly ImmutableDictionary<string, IWitWorkerValueSourceWriter> _values = values ?? throw new ArgumentNullException(nameof(values));

    public WitWorkerClientSource Write(WitWorkerClientRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.HostingJavaScriptRoot)
            || string.IsNullOrWhiteSpace(request.WorkerFileName) || Path.GetFileName(request.WorkerFileName) != request.WorkerFileName
            || request.WorkerFileName.Contains('\\') || !IsDigest(request.BuildFingerprint) || !IsDigest(request.ManifestSha256))
            throw new ArgumentException("WIT worker client publication contract is invalid", nameof(request));
        var contract = _contracts.Read(request.ContractJson);
        var layouts = _layouts.Plan(contract);
        if (contract.Exports.Any(export => export!.Kind!.Name != "freestanding")
            || layouts.Values.Any(layout => !_values.ContainsKey(layout.Kind)))
            throw new NotSupportedException("Resource-bearing application exports are unsupported by Web Workers");
        var js = new StringBuilder();
        var ts = new StringBuilder();
        var module = new Uri(Path.Combine(request.HostingJavaScriptRoot, "worker-session-client.mjs")).AbsoluteUri;
        js.AppendLine(CultureInfo.InvariantCulture, $"import {{ createWorkerSessionClient }} from {Json(module)};");
        js.AppendLine(CultureInfo.InvariantCulture, $"export const workerContractFingerprint = {Json(Convert.ToHexStringLower(SHA256.HashData(request.ContractJson.Span)))};");
        ts.AppendLine("export declare const workerContractFingerprint: string;");
        ts.AppendLine("export interface NetWasmWorkerStartupOptions { signal?: AbortSignal; onNotification?: (value: { operation: string; arguments: unknown[] }) => void | Promise<void>; }");
        ts.AppendLine("export interface NetWasmWorkerCallOptions { signal?: AbortSignal; timeoutMilliseconds?: number; }");
        foreach (var (id, layout) in layouts.OrderBy(entry => entry.Key))
        {
            var source = _values[layout.Kind].Write(new(id, layout, layouts));
            js.AppendLine(CultureInfo.InvariantCulture, $"function copyType{id}(value) {{ {source.JavaScript} }}");
            ts.AppendLine(CultureInfo.InvariantCulture, $"export type {layout.Name} = {source.TypeScript};");
        }
        js.AppendLine(PrimitiveCopies);
        js.AppendLine("export async function createWorker(options = {}) {");
        js.AppendLine("const signal = options.signal; if (signal?.aborted) throw signal.reason;");
        js.AppendLine("const onNotification = options.onNotification ?? null;");
        js.AppendLine("if (onNotification !== null && typeof onNotification !== 'function') throw new TypeError('worker notification handler must be a function');");
        js.AppendLine(CultureInfo.InvariantCulture, $"const worker = new Worker(new URL({Json("./" + request.WorkerFileName)}, import.meta.url), {{ type: 'module' }});");
        js.AppendLine("const session = createWorkerSessionClient({");
        js.AppendLine("generation: crypto.randomUUID(), worker, onNotification,");
        js.AppendLine(CultureInfo.InvariantCulture, $"operations: {System.Text.Json.JsonSerializer.Serialize(contract.Exports.Select(export => export!.Operation))},");
        js.AppendLine(CultureInfo.InvariantCulture, $"startup: {{ buildFingerprint: {Json(request.BuildFingerprint)}, manifestSha256: {Json(request.ManifestSha256)} }},");
        js.AppendLine("}); const abort = () => session.terminate(); signal?.addEventListener('abort', abort, { once: true });");
        js.AppendLine("try { if (signal?.aborted) abort(); await session.ready; } finally { signal?.removeEventListener('abort', abort); } return Object.freeze({");
        ts.AppendLine("export interface NetWasmWorkerClient {");
        var names = new WitWorkerValueSourceRequest(0, new("", "", default, false, null), layouts);
        foreach (var export in contract.Exports)
        {
            var parameters = string.Join(", ", export!.Parameters.Select((_, index) => $"argument{index}"));
            var copies = export.Parameters.Select((parameter, index) => WitWorkerValueSourceRequest.Copy(parameter!.Type!, $"argument{index}"));
            js.AppendLine(CultureInfo.InvariantCulture, $"[{Json(export.Operation!)}]: ({(parameters.Length == 0 ? "" : parameters + ", ")}options) => {{");
            js.AppendLine(CultureInfo.InvariantCulture, $"try {{ const args = [{string.Join(", ", copies)}]; return session.invoke({Json(export.Operation!)}, args, options); }}");
            js.AppendLine("catch (cause) { return Promise.reject(cause); } },");
            var parameterTypes = export.Parameters.Select((parameter, index) => $"argument{index}: {names.Type(parameter!.Type!)}")
                .Append("options?: NetWasmWorkerCallOptions");
            ts.AppendLine(CultureInfo.InvariantCulture, $"{Json(export.Operation!)}({string.Join(", ", parameterTypes)}): Promise<{(export.Result is null ? "void" : names.Type(export.Result))}>;");
        }
        js.AppendLine("dispose: () => session.dispose(), terminate: () => session.terminate(),");
        js.AppendLine("}); }");
        ts.AppendLine("dispose(): Promise<void>; terminate(): void; }");
        ts.AppendLine("export declare function createWorker(options?: NetWasmWorkerStartupOptions): Promise<NetWasmWorkerClient>;");
        return new(Encoding.UTF8.GetBytes(js.ToString().ReplaceLineEndings("\n")),
            Encoding.UTF8.GetBytes(ts.ToString().ReplaceLineEndings("\n")));
    }

    private static string Json(string value) => WitWorkerValueSourceRequest.Json(value);
    private static bool IsDigest(string value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private const string PrimitiveCopies = """
        function invalidValue() { return new TypeError('WIT worker argument does not match its declared type'); }
        function objectData(value) {
          if (value === null || typeof value !== 'object' || Array.isArray(value) || ArrayBuffer.isView(value)) throw invalidValue();
          return Object.getOwnPropertyDescriptors(value);
        }
        function property(fields, name, optional = false) {
          const descriptor = Object.hasOwn(fields, name) ? fields[name] : undefined;
          if (descriptor === undefined && optional) return undefined;
          if (descriptor === undefined || !Object.hasOwn(descriptor, 'value')) throw invalidValue();
          return descriptor.value;
        }
        function arrayData(value, length = undefined) {
          if (!Array.isArray(value)) throw invalidValue();
          const fields = Object.getOwnPropertyDescriptors(value);
          const size = property(fields, 'length');
          if (length !== undefined && size !== length) throw invalidValue();
          return Array.from({ length: size }, (_, index) => property(fields, String(index), true));
        }
        const typedPrototype = Object.getPrototypeOf(Uint8Array.prototype);
        const typedName = Object.getOwnPropertyDescriptor(typedPrototype, Symbol.toStringTag).get;
        const typedLength = Object.getOwnPropertyDescriptor(typedPrototype, 'length').get;
        const typedSet = typedPrototype.set;
        function typedData(value, name, Constructor) {
          if (!ArrayBuffer.isView(value) || typedName.call(value) !== name) throw invalidValue();
          const copy = new Constructor(typedLength.call(value));
          typedSet.call(copy, value); return copy;
        }
        function copybool(value) { if (typeof value !== 'boolean') throw invalidValue(); return value; }
        function optionalBool(value) { return value === undefined ? undefined : copybool(value); }
        function integer(value, minimum, maximum) {
          if (!Number.isInteger(value) || value < minimum || value > maximum) throw invalidValue(); return value;
        }
        function copys8(value) { return integer(value, -128, 127); }
        function copyu8(value) { return integer(value, 0, 255); }
        function copys16(value) { return integer(value, -32768, 32767); }
        function copyu16(value) { return integer(value, 0, 65535); }
        function copys32(value) { return integer(value, -2147483648, 2147483647); }
        function copyu32(value) { return integer(value, 0, 4294967295); }
        function bigInteger(value, minimum, maximum) {
          if (typeof value !== 'bigint' || value < minimum || value > maximum) throw invalidValue(); return value;
        }
        function copys64(value) { return bigInteger(value, -9223372036854775808n, 9223372036854775807n); }
        function copyu64(value) { return bigInteger(value, 0n, 18446744073709551615n); }
        function copyf32(value) { if (typeof value !== 'number') throw invalidValue(); return value; }
        const copyf64 = copyf32;
        function copystring(value) { if (typeof value !== 'string') throw invalidValue(); return value; }
        function copychar(value) {
          copystring(value); const point = value.codePointAt(0);
          if (point === undefined || (point >= 0xd800 && point <= 0xdfff) || value.length !== (point > 0xffff ? 2 : 1)) throw invalidValue();
          return value;
        }
        """;
}

public static class WitWorkerClientComposition
{
    public static IWitWorkerClientWriter CreateWriter() => new WitWorkerClientWriter(
        WitWorkerContractComposition.CreateReader(), new WitWorkerValueLayoutPlanner(), WitWorkerValueSourceComposition.CreateWriters());
}
