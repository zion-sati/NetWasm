using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Deployment;

public sealed record WitWorkerComponentAdapterRequest(
    ReadOnlyMemory<byte> ContractJson,
    string JcoVersion,
    ImmutableArray<WitWorkerRootExport> RootExports);

public sealed record WitWorkerRootExport(string Name, string Kind);

public interface IWitWorkerComponentAdapterWriter
{
    byte[] Write(WitWorkerComponentAdapterRequest request);
}

/// <summary>Emits exact pinned-jco accessors from one build-bound WIT worker contract.</summary>
public sealed class WitWorkerComponentAdapterWriter(
    IWitWorkerContractReader contracts, IWitWorkerValueLayoutPlanner layouts) : IWitWorkerComponentAdapterWriter
{
    private readonly IWitWorkerContractReader _contracts = contracts ??
        throw new ArgumentNullException(nameof(contracts));
    private readonly IWitWorkerValueLayoutPlanner _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public const string ContractKey = "netwasm:worker/wit@1.0.0";
    private const string ReactorGuestInterface = "netwasm:runtime/reactor-guest@1.0.0";
    private const string ReactorHostModule = "netwasm:runtime/reactor-host";
    private const string VersionedReactorHostModule =
        "netwasm:runtime/reactor-host@1.0.0";
    public byte[] Write(WitWorkerComponentAdapterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.JcoVersion != CanonicalComponentAdapterWriter.SupportedJcoVersion)
        {
            throw new NotSupportedException(
                "The generated WIT worker adapter requires the pinned jco version.");
        }
        if (request.ContractJson.IsEmpty)
        {
            throw new ArgumentException(
                "A build-bound WIT worker contract is required.", nameof(request));
        }

        var contract = _contracts.Read(request.ContractJson);
        var layouts = _layouts.Plan(contract);
        if (contract.Exports.Any(export => export!.Kind!.Name != "freestanding")
            || layouts.Values.Any(layout => layout.Kind is "handle" or "resource"))
            throw new NotSupportedException("Resource-bearing application exports are unsupported by Web Workers");
        var rootExports = ValidateRootExports(request.RootExports);

        var source = new StringBuilder();
        AppendLine(source, $"// Generated for jco {CanonicalComponentAdapterWriter.SupportedJcoVersion}.");
        AppendLine(source, $"export const contractKey = {Json(ContractKey)};");
        AppendLine(source);
        AppendLine(source, "export function createAdapter(generatedModule) {");
        AppendLine(source, "  const instantiateGenerated = bindGeneratedModule(generatedModule);");
        if (contract.Exports.Any(export => IsDeclaredResult(export!.Result, contract, layouts)))
        {
            AppendLine(source, "  const isComponentError = generatedModule._util?.isComponentError;");
            AppendLine(source, "  if (typeof isComponentError !== 'function') throw new TypeError('pinned jco declared-error identity is unavailable');");
        }
        AppendLine(source, "  return Object.freeze({");
        AppendLine(source, "    contractKey,");
        AppendLine(source, "    async instantiate(request = {}) {");
        if (contract.Reactor is null)
        {
            AppendLine(source, "      const { loadCoreModule, imports, instantiateCore } = validateRequest(request);");
            AppendLine(source, "      const root = await instantiateGenerated(loadCoreModule, imports, instantiateCore);");
        }
        else
        {
            AppendLine(source, "      const { loadCoreModule, imports, instantiateCore, reactorHost } = validateRequest(request);");
            AppendLine(source, "      const componentImports = bindReactorHost(imports, reactorHost);");
            AppendLine(source, "      const root = await instantiateGenerated(loadCoreModule, componentImports, instantiateCore);");
        }
        AppendLine(source, "      if (root === null || typeof root !== \"object\") {");
        AppendLine(source, "        throw new TypeError(\"pinned jco WIT worker exports are unavailable\");");
        AppendLine(source, "      }");
        if (contract.Reactor is { } reactor)
        {
            var reactorRoot = ResolveInterfaceRoot(
                reactor.JavaScriptRoot!,
                reactor.Interface!,
                rootExports);
            AppendLine(source, $"      const reactorGuest = root[{Json(reactorRoot)}];");
            AppendLine(source, "      if (reactorGuest === null || typeof reactorGuest !== \"object\"");
            AppendLine(source, $"          || typeof reactorGuest[{Json(reactor.JavaScriptMember!)}] !== \"function\") {{");
            AppendLine(source, "        throw new TypeError(\"pinned jco reactor-guest export is unavailable\");");
            AppendLine(source, "      }");
            AppendLine(source, $"      const guestWake = reactorGuest[{Json(reactor.JavaScriptMember!)}].bind(reactorGuest);");
        }
        else
        {
            AppendLine(source, "      const guestWake = null;");
        }
        AppendLine(source, "      const exports = Object.create(null);");
        for (var index = 0; index < contract.Exports.Length; index++)
        {
            var export = contract.Exports[index]!;
            var binding = $"binding{index}";
            var rootExpression = export.Placement == "root"
                ? "root"
                : $"root[{Json(ResolveInterfaceRoot(
                    export.JavaScriptRoot,
                    export.Interface!,
                    rootExports))}]";
            if (export.Placement == "root")
            {
                if (!rootExports.TryGetValue(
                    export.JavaScriptMember!,
                    out var rootKind))
                {
                    throw Invalid(
                        $"Pinned jco root export '{export.JavaScriptMember}' is unavailable");
                }
                if (rootKind != "function")
                {
                    throw Invalid(
                        $"Pinned jco root export '{export.JavaScriptMember}' has the wrong kind");
                }
            }
            AppendLine(source, $"      const {binding} = {rootExpression};");
            if (export.Placement == "interface")
            {
                AppendLine(source, $"      if ({binding} === null || typeof {binding} !== \"object\") {{");
                AppendLine(source, $"        throw new TypeError({Json($"pinned jco interface export '{export.Interface}' is unavailable")});");
                AppendLine(source, "      }");
            }
            AppendLine(source, $"      if (typeof {binding}[{Json(export.JavaScriptMember!)}] !== \"function\") {{");
            AppendLine(source, $"        throw new TypeError({Json($"pinned jco WIT export '{export.Operation}' is unavailable")});");
            AppendLine(source, "      }");
            if (IsDeclaredResult(export.Result, contract, layouts))
            {
                AppendLine(source, $"      exports[{Json(export.Operation!)}] = (...args) => {{");
                AppendLine(source, $"        try {{ return {{ tag: 'ok', val: {binding}[{Json(export.JavaScriptMember!)}](...args) }}; }}");
                AppendLine(source, "        catch (cause) { if (!isComponentError(cause)) throw cause; return { tag: 'err', val: cause.payload }; }");
                AppendLine(source, "      };");
            }
            else
            {
                AppendLine(source, $"      exports[{Json(export.Operation!)}] = (...args) =>");
                AppendLine(source, $"        {binding}[{Json(export.JavaScriptMember!)}](...args);");
            }
        }
        AppendLine(source, "      return Object.freeze({");
        AppendLine(source, "        exports: Object.freeze(exports),");
        AppendLine(source, "        guestWake,");
        AppendLine(source, "      });");
        AppendLine(source, "    },");
        AppendLine(source, "  });");
        AppendLine(source, "}");
        AppendLine(source);
        AppendLine(source, "function bindGeneratedModule(generatedModule) {");
        AppendLine(source, "  if (generatedModule === null || typeof generatedModule !== \"object\"");
        AppendLine(source, "      || typeof generatedModule.instantiate !== \"function\") {");
        AppendLine(source, "    throw new TypeError(\"verified generated jco module is required\");");
        AppendLine(source, "  }");
        AppendLine(source, "  return generatedModule.instantiate.bind(generatedModule);");
        AppendLine(source, "}");
        AppendLine(source);
        AppendLine(source, "function validateRequest(request) {");
        AppendLine(source, "  if (request === null || typeof request !== \"object\" || Array.isArray(request)) {");
        AppendLine(source, "    throw new TypeError(\"component adapter instantiation request is required\");");
        AppendLine(source, "  }");
        AppendLine(source, "  const { loadCoreModule, imports, instantiateCore, reactorHost } = request;");
        AppendLine(source, "  if (typeof loadCoreModule !== \"function\") {");
        AppendLine(source, "    throw new TypeError(\"component core-module loader is required\");");
        AppendLine(source, "  }");
        AppendLine(source, "  if (imports === null || typeof imports !== \"object\" || Array.isArray(imports)");
        AppendLine(source, "      || Object.values(Object.getOwnPropertyDescriptors(imports)).some(");
        AppendLine(source, "        descriptor => !descriptor.enumerable || !(\"value\" in descriptor))) {");
        AppendLine(source, "    throw new TypeError(\"component imports are required\");");
        AppendLine(source, "  }");
        AppendLine(source, "  if (instantiateCore !== undefined && typeof instantiateCore !== \"function\") {");
        AppendLine(source, "    throw new TypeError(\"component core-module instantiator is invalid\");");
        AppendLine(source, "  }");
        AppendLine(source, "  return { loadCoreModule, imports, instantiateCore, reactorHost };");
        AppendLine(source, "}");
        if (contract.Reactor is not null)
        {
            AppendLine(source);
            AppendLine(source, "function bindReactorHost(imports, reactorHost) {");
            AppendLine(source, $"  if (Object.hasOwn(imports, {Json(ReactorHostModule)})");
            AppendLine(source, $"      || Object.hasOwn(imports, {Json(VersionedReactorHostModule)})) {{");
            AppendLine(source, "    throw new TypeError(\"component reactor host is a reserved import\");");
            AppendLine(source, "  }");
            AppendLine(source, "  if (reactorHost === null || typeof reactorHost !== \"object\"");
            AppendLine(source, "      || typeof reactorHost.assertAvailable !== \"function\"");
            AppendLine(source, "      || typeof reactorHost.watch !== \"function\"");
            AppendLine(source, "      || typeof reactorHost.cancel !== \"function\") {");
            AppendLine(source, "    throw new TypeError(\"component reactor host is unavailable\");");
            AppendLine(source, "  }");
            AppendLine(source, "  const binding = Object.freeze({");
            AppendLine(source, "    watch(ready, token) {");
            AppendLine(source, "      reactorHost.assertAvailable();");
            AppendLine(source, "      return reactorHost.watch(ready, token);");
            AppendLine(source, "    },");
            AppendLine(source, "    cancel(token) {");
            AppendLine(source, "      reactorHost.assertAvailable();");
            AppendLine(source, "      return reactorHost.cancel(token);");
            AppendLine(source, "    },");
            AppendLine(source, "  });");
            AppendLine(source, "  return Object.freeze(Object.assign(Object.create(null), imports, {");
            AppendLine(source, $"    [{Json(ReactorHostModule)}]: binding,");
            AppendLine(source, "  }));");
            AppendLine(source, "}");
        }
        return Encoding.UTF8.GetBytes(source.ToString());
    }

    private static ImmutableDictionary<string, string> ValidateRootExports(
        ImmutableArray<WitWorkerRootExport> exports)
    {
        if (exports.IsDefault)
        {
            throw Invalid("Pinned jco root export metadata is required");
        }
        var result = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        foreach (var export in exports)
        {
            if (export is null || string.IsNullOrWhiteSpace(export.Name)
                || export.Kind is not ("function" or "instance")
                || !result.TryAdd(export.Name, export.Kind))
            {
                throw Invalid("Pinned jco root export metadata is invalid");
            }
        }
        return result.ToImmutable();
    }

    private static string ResolveInterfaceRoot(
        string candidate,
        string interfaceName,
        ImmutableDictionary<string, string> rootExports)
    {
        if (rootExports.TryGetValue(interfaceName, out var interfaceKind))
        {
            if (interfaceKind != "instance")
            {
                throw Invalid(
                    $"Pinned jco root export '{interfaceName}' has the wrong kind");
            }
            return interfaceName;
        }
        if (rootExports.TryGetValue(candidate, out var candidateKind))
        {
            if (candidateKind != "instance")
            {
                throw Invalid($"Pinned jco root export '{candidate}' has the wrong kind");
            }
            return candidate;
        }
        throw Invalid($"Pinned jco interface export '{interfaceName}' is unavailable");
    }

    private static bool IsDeclaredResult(WitWorkerValueReference? reference,
        WitWorkerResolvedContract contract, ImmutableDictionary<int, WitWorkerValueLayout> layouts)
    {
        while (reference?.Definition is { } id)
        {
            if (layouts[id].Kind == "result") return true;
            if (layouts[id].Kind != "type") return false;
            reference = contract.References[id][0];
        }
        return false;
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new(message, inner);

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static void AppendLine(StringBuilder source, string value = "") =>
        source.Append(value).Append('\n');

}
