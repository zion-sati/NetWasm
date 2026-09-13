using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal sealed class TemporaryResultChannelFactory : ITemporaryResultChannelFactory
{
    public ITemporaryResultChannel Create() => new TemporaryResultChannel(
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netwasm-{Guid.NewGuid():N}.result.json"));
}

internal sealed class TemporaryResultChannel(string path) : ITemporaryResultChannel
{
    public string Path { get; } = path;

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(Path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException(
                "The NetWasm artifact host did not complete its structured result channel.",
                exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(Path);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
