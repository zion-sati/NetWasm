// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm: pay-for-play for BrotliStream. Every program that writes or reads any stream reaches BrotliStream's Write and
// Read overrides virtually, and upstream holds the BrotliEncoder and BrotliDecoder structs in the stream and calls them
// directly, so a program that only decompresses kept Brotli's encoder (about 700 KB of native code and tables) and one
// that only compresses kept the decoder. BrotliStream holds each through these operations instead: only its
// constructor's branch for that direction allocates one, so in a Release build, where the constant mode reaches the
// shared constructor, the other direction's operations are never allocated and their calls have no target. The members
// are the structs' own, forwarded unchanged.

using System.Buffers;

namespace System.IO.Compression
{
    internal abstract class BrotliEncoderOperations
    {
        public static BrotliEncoderOperations Create() => new Linked();

        public abstract void SetQuality(int quality);
        public abstract void SetWindow(int window);
        public abstract OperationStatus Compress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock);
        public abstract OperationStatus Flush(Span<byte> destination, out int bytesWritten);
        public abstract OperationStatus Compress(ReadOnlyMemory<byte> source, Memory<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock);
        public abstract OperationStatus Flush(Memory<byte> destination, out int bytesWritten);
        // Upstream's "_encoder._state == null || _encoder._state.IsClosed".
        public abstract bool IsClosed { get; }
        public abstract void Dispose();

        private sealed class Linked : BrotliEncoderOperations
        {
            private BrotliEncoder _encoder;

            public override void SetQuality(int quality) => _encoder.SetQuality(quality);
            public override void SetWindow(int window) => _encoder.SetWindow(window);
            public override OperationStatus Compress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock) =>
                _encoder.Compress(source, destination, out bytesConsumed, out bytesWritten, isFinalBlock);
            public override OperationStatus Flush(Span<byte> destination, out int bytesWritten) => _encoder.Flush(destination, out bytesWritten);
            public override OperationStatus Compress(ReadOnlyMemory<byte> source, Memory<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock) =>
                _encoder.Compress(source, destination, out bytesConsumed, out bytesWritten, isFinalBlock);
            public override OperationStatus Flush(Memory<byte> destination, out int bytesWritten) => _encoder.Flush(destination, out bytesWritten);
            public override bool IsClosed => _encoder._state == null || _encoder._state.IsClosed;
            public override void Dispose() => _encoder.Dispose();
        }
    }

    internal abstract class BrotliDecoderOperations
    {
        public static BrotliDecoderOperations Create() => new Linked();

        public abstract OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten);
        public abstract void Dispose();

        private sealed class Linked : BrotliDecoderOperations
        {
            private BrotliDecoder _decoder;

            public override OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten) =>
                _decoder.Decompress(source, destination, out bytesConsumed, out bytesWritten);
            public override void Dispose() => _decoder.Dispose();
        }
    }
}
