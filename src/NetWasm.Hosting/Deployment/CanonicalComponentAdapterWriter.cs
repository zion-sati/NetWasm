using System;
using System.Text;

namespace NetWasm.Hosting.Deployment;

/// <summary>Emits an alias-free adapter over the exact pinned jco output shape.</summary>
public sealed class CanonicalComponentAdapterWriter : ICanonicalComponentAdapterWriter
{
    public const string SupportedJcoVersion = "1.28.1";
    public const string CommandContract = "wasi-command@0.2.11";
    public const string ProcessContract = "netwasm:runtime/process@1.0.0";

    public byte[] Write(CanonicalComponentAdapterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.JcoVersion != SupportedJcoVersion)
        {
            throw new NotSupportedException("The generated component adapter requires the pinned jco version.");
        }

        var source = request.ContractKey switch
        {
            CommandContract => CommandSource(),
            ProcessContract => ProcessSource(),
            _ => throw new NotSupportedException("The component execution contract has no generated adapter."),
        };
        return Encoding.UTF8.GetBytes(source.ReplaceLineEndings("\n") + "\n");
    }

    private static string CommandSource() => """
        export const contractKey = "wasi-command@0.2.11";

        export function createAdapter(generatedModule) {
          const instantiateGenerated = bindGeneratedModule(generatedModule);
          return Object.freeze({
            contractKey,
            async instantiate(request = {}) {
              const { loadCoreModule, imports, instantiateCore } = validateRequest(request);
              const root = await instantiateGenerated(loadCoreModule, imports, instantiateCore);
              const command = root?.["wasi:cli/run@0.2.11"];
              if (command === null || typeof command !== "object" || typeof command.run !== "function") {
                throw new TypeError("pinned jco command export is unavailable");
              }
              return Object.freeze({
                command: Object.freeze({
                  run() {
                    try {
                      command.run();
                      return 0;
                    } catch (error) {
                      if (isWasiCommandError(error)) return 1;
                      throw error;
                    }
                  },
                }),
              });
            },
          });
        }

        function bindGeneratedModule(generatedModule) {
          if (generatedModule === null || typeof generatedModule !== "object"
              || typeof generatedModule.instantiate !== "function") {
            throw new TypeError("verified generated jco module is required");
          }
          return generatedModule.instantiate.bind(generatedModule);
        }

        function isWasiCommandError(error) {
          return error !== null && typeof error === "object"
            && Object.hasOwn(error, "payload") && error.payload === undefined
            && Object.getPrototypeOf(error)?.constructor?.name === "ComponentError";
        }

        function validateRequest(request) {
          if (request === null || typeof request !== "object" || Array.isArray(request)) {
            throw new TypeError("component adapter instantiation request is required");
          }
          const { loadCoreModule, imports, instantiateCore } = request;
          if (typeof loadCoreModule !== "function") {
            throw new TypeError("component core-module loader is required");
          }
          if (imports === null || typeof imports !== "object" || Array.isArray(imports)) {
            throw new TypeError("component imports are required");
          }
          if (instantiateCore !== undefined && typeof instantiateCore !== "function") {
            throw new TypeError("component core-module instantiator is invalid");
          }
          return { loadCoreModule, imports, instantiateCore };
        }
        """;

    private static string ProcessSource() => """
        export const contractKey = "netwasm:runtime/process@1.0.0";

        export function createAdapter(generatedModule) {
          const instantiateGenerated = bindGeneratedModule(generatedModule);
          return Object.freeze({
            contractKey,
            async instantiate(request = {}) {
              const { loadCoreModule, imports, instantiateCore } = validateRequest(request);
              const root = await instantiateGenerated(loadCoreModule, imports, instantiateCore);
              if (root === null || typeof root !== "object") {
                throw new TypeError("pinned jco process exports are unavailable");
              }
              return Object.freeze({ process: root.process, reactorGuest: root.reactorGuest });
            },
          });
        }

        function bindGeneratedModule(generatedModule) {
          if (generatedModule === null || typeof generatedModule !== "object"
              || typeof generatedModule.instantiate !== "function") {
            throw new TypeError("verified generated jco module is required");
          }
          return generatedModule.instantiate.bind(generatedModule);
        }

        function validateRequest(request) {
          if (request === null || typeof request !== "object" || Array.isArray(request)) {
            throw new TypeError("component adapter instantiation request is required");
          }
          const { loadCoreModule, imports, instantiateCore } = request;
          if (typeof loadCoreModule !== "function") {
            throw new TypeError("component core-module loader is required");
          }
          if (imports === null || typeof imports !== "object" || Array.isArray(imports)) {
            throw new TypeError("component imports are required");
          }
          if (instantiateCore !== undefined && typeof instantiateCore !== "function") {
            throw new TypeError("component core-module instantiator is invalid");
          }
          return { loadCoreModule, imports, instantiateCore };
        }
        """;
}
