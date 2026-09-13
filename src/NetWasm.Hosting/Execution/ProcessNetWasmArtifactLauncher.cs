using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal sealed class ProcessNetWasmArtifactLauncher(
    IArtifactProcessRunner processes,
    ITemporaryResultChannelFactory resultChannels,
    INetWasmExecutionResultReader results) : INetWasmArtifactLauncher
{
    private readonly IArtifactProcessRunner _processes = processes ??
        throw new ArgumentNullException(nameof(processes));
    private readonly ITemporaryResultChannelFactory _resultChannels = resultChannels ??
        throw new ArgumentNullException(nameof(resultChannels));
    private readonly INetWasmExecutionResultReader _results = results ??
        throw new ArgumentNullException(nameof(results));

    public async ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ExecutionDescriptor descriptor,
        NetWasmArtifactPaths paths,
        ImmutableArray<string> arguments,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(paths);
        await using var resultChannel = _resultChannels.Create();
        var processArguments = ImmutableArray.CreateBuilder<string>(9 + arguments.Length);
        processArguments.Add(descriptor.LauncherPath);
        processArguments.Add("--descriptor");
        processArguments.Add(paths.DescriptorPath);
        processArguments.Add("--request");
        processArguments.Add(paths.RequestPath);
        processArguments.Add("--result");
        processArguments.Add(resultChannel.Path);
        processArguments.Add("--");
        processArguments.AddRange(arguments);

        var exitCode = await _processes.RunAsync(
            new ArtifactProcessStart(
                descriptor.HostExecutablePath,
                processArguments.ToImmutable(),
                System.IO.Path.GetDirectoryName(paths.SourcePath)!),
            standardOutput,
            standardError,
            cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                "The NetWasm artifact host exited before completing its structured result channel.");
        }

        var result = await resultChannel.ReadAsync(cancellationToken).ConfigureAwait(false);
        return _results.Read(result);
    }
}
