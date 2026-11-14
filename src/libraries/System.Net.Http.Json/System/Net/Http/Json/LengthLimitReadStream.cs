// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.Json;

internal sealed class LengthLimitReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    internal LengthLimitReadStream(Stream inner, long limit)
    {
        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        CheckLimit(read);
        return read;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = _inner.ReadAsync(buffer, cancellationToken);
        if (read.IsCompletedSuccessfully)
        {
            var count = read.Result;
            CheckLimit(count);
            return new ValueTask<int>(count);
        }

        return AwaitReadAsync(read);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    private async ValueTask<int> AwaitReadAsync(ValueTask<int> read)
    {
        var count = await read.ConfigureAwait(false);
        CheckLimit(count);
        return count;
    }

    private void CheckLimit(int count)
    {
        _read += count;
        if (_read > _limit)
        {
            throw new HttpRequestException(
                "The response content exceeded the configured buffer limit.");
        }
    }
}
