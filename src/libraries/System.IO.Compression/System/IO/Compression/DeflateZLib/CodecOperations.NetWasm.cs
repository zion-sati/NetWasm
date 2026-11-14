// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm: pay-for-play for DeflateStream (and so GZipStream, ZLibStream and ZIP entries). Every program that writes
// or reads any stream reaches DeflateStream's Write and Read overrides virtually, and upstream calls the sealed
// Deflater and Inflater directly from them, so a program that only decompresses kept zlib's deflate (and one that only
// compresses kept inflate). DeflateStream holds each codec through these operations instead: only the constructor for
// its direction allocates the wrapper, so in a Release build, where the constant mode reaches the shared constructor,
// the other direction's wrapper is never allocated and its calls have no target. The members are Deflater's and
// Inflater's own, forwarded unchanged.

namespace System.IO.Compression
{
    internal abstract class DeflaterOperations : IDisposable
    {
        public static DeflaterOperations Of(Deflater deflater) => new Linked(deflater);

        public abstract void Dispose();
        public abstract bool NeedsInput();
        internal abstract void SetInput(ReadOnlyMemory<byte> inputBuffer);
        internal abstract unsafe void SetInput(byte* inputBufferPtr, int count);
        internal abstract int GetDeflateOutput(byte[] outputBuffer);
        internal abstract bool Finish(byte[] outputBuffer, out int bytesRead);
        internal abstract bool Flush(byte[] outputBuffer, out int bytesRead);

        private sealed class Linked(Deflater deflater) : DeflaterOperations
        {
            public override void Dispose() => deflater.Dispose();
            public override bool NeedsInput() => deflater.NeedsInput();
            internal override void SetInput(ReadOnlyMemory<byte> inputBuffer) => deflater.SetInput(inputBuffer);
            internal override unsafe void SetInput(byte* inputBufferPtr, int count) => deflater.SetInput(inputBufferPtr, count);
            internal override int GetDeflateOutput(byte[] outputBuffer) => deflater.GetDeflateOutput(outputBuffer);
            internal override bool Finish(byte[] outputBuffer, out int bytesRead) => deflater.Finish(outputBuffer, out bytesRead);
            internal override bool Flush(byte[] outputBuffer, out int bytesRead) => deflater.Flush(outputBuffer, out bytesRead);
        }
    }

    internal abstract class InflaterOperations : IDisposable
    {
        public static InflaterOperations Of(Inflater inflater) => new Linked(inflater);

        public abstract void Dispose();
        public abstract bool Finished();
        public abstract bool Inflate(out byte b);
        public abstract int Inflate(byte[] bytes, int offset, int length);
        public abstract int Inflate(Span<byte> destination);
        public abstract bool NeedsInput();
        public abstract bool NonEmptyInput();
        public abstract void SetInput(byte[] inputBuffer, int startIndex, int count);
        public abstract void SetInput(ReadOnlyMemory<byte> inputBuffer);
        internal abstract bool IsGzipStream();
        internal abstract int GetAvailableInput();

        private sealed class Linked(Inflater inflater) : InflaterOperations
        {
            public override void Dispose() => inflater.Dispose();
            public override bool Finished() => inflater.Finished();
            public override bool Inflate(out byte b) => inflater.Inflate(out b);
            public override int Inflate(byte[] bytes, int offset, int length) => inflater.Inflate(bytes, offset, length);
            public override int Inflate(Span<byte> destination) => inflater.Inflate(destination);
            public override bool NeedsInput() => inflater.NeedsInput();
            public override bool NonEmptyInput() => inflater.NonEmptyInput();
            public override void SetInput(byte[] inputBuffer, int startIndex, int count) => inflater.SetInput(inputBuffer, startIndex, count);
            public override void SetInput(ReadOnlyMemory<byte> inputBuffer) => inflater.SetInput(inputBuffer);
            internal override bool IsGzipStream() => inflater.IsGzipStream();
            internal override int GetAvailableInput() => inflater.GetAvailableInput();
        }
    }
}
