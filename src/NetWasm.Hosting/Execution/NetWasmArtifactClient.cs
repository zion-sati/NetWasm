using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

/// <summary>Default client for executing an SDK-built NetWasm artifact.</summary>
public sealed class NetWasmArtifactClient : INetWasmArtifactClient
{
    private readonly INetWasmArtifactPathResolver _paths;
    private readonly IArtifactDocumentReader _documents;
    private readonly IExecutionDescriptorReader _descriptors;
    private readonly INetWasmExecutionRequestReader _requests;
    private readonly INetWasmArtifactLauncher _launcher;

    public NetWasmArtifactClient()
        : this(
            new NetWasmArtifactPathResolver(),
            new FileArtifactDocumentReader(),
            new ExecutionDescriptorReader(new ExecutionDescriptorValidator()),
            new NetWasmExecutionRequestReader(new NetWasmExecutionRequestValidator()),
            new ProcessNetWasmArtifactLauncher(
                new ArtifactProcessRunner(),
                new TemporaryResultChannelFactory(),
                new NetWasmExecutionResultReader(new NetWasmExecutionResultValidator())))
    {
    }

    internal NetWasmArtifactClient(
        INetWasmArtifactPathResolver paths,
        IArtifactDocumentReader documents,
        IExecutionDescriptorReader descriptors,
        INetWasmExecutionRequestReader requests,
        INetWasmArtifactLauncher launcher)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public async ValueTask<NetWasmExecutionResult> ExecuteAsync(
        NetWasmArtifactExecutionRequest request,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Arguments.IsDefault)
        {
            throw new ArgumentException("NetWasm artifact arguments must be explicit.", nameof(request));
        }
        foreach (var argument in request.Arguments)
        {
            if (argument is null || argument.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "NetWasm artifact arguments cannot be null or contain NUL.",
                    nameof(request));
            }
        }

        var paths = _paths.Resolve(request.SourcePath);
        var descriptorBytes = await _documents.ReadAsync(paths.DescriptorPath, cancellationToken)
            .ConfigureAwait(false);
        var requestBytes = await _documents.ReadAsync(paths.RequestPath, cancellationToken)
            .ConfigureAwait(false);
        var descriptor = _descriptors.Read(descriptorBytes);
        var baseRequest = _requests.Read(requestBytes);
        if (!string.Equals(
                descriptor.BuildFingerprint,
                baseRequest.BuildFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                descriptor.DeploymentManifestSha256,
                baseRequest.DeploymentManifestSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The NetWasm execution descriptor and request identify different builds.");
        }

        return await _launcher.ExecuteAsync(
            descriptor,
            paths,
            request.Arguments,
            standardOutput,
            standardError,
            cancellationToken).ConfigureAwait(false);
    }
}
