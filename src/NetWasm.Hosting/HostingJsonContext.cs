using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting;

// Hosting's JSON contracts. Each reader and writer passes its own options.
[JsonSerializable(typeof(DeploymentManifest))]
[JsonSerializable(typeof(ExecutionDescriptor))]
[JsonSerializable(typeof(NetWasmExecutionRequest))]
[JsonSerializable(typeof(NetWasmExecutionResult))]
[JsonSerializable(typeof(WitWorkerContractArtifact))]
[JsonSerializable(typeof(string))]
[ExcludeFromCodeCoverage]
internal sealed partial class HostingJsonContext : JsonSerializerContext;
