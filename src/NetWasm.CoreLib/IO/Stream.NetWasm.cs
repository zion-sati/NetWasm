// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm counterpart of dotnet/runtime deae2e32's Stream.Mono.cs and Stream.NativeAot.cs, where these are runtime
// intrinsics that ask whether the stream's type overrides BeginRead/EndRead (BeginWrite/EndWrite). NetWasm answers the
// same question with delegates: one created from this.BeginRead binds through virtual dispatch to the type's override,
// and its method is compared with Stream's own, which StreamMethods takes through base.BeginRead (a non-virtual ldftn).
// A stream that overrides neither then gets upstream's shortcut, whose ReadWriteTask a synchronous wait runs inline.

namespace System.IO
{
    public abstract partial class Stream
    {
        private bool HasOverriddenBeginEndRead()
        {
            Func<byte[], int, int, AsyncCallback?, object?, IAsyncResult> begin = BeginRead;
            Func<IAsyncResult, int> end = EndRead;
            return begin.MethodId != StreamMethods.BeginReadId || end.MethodId != StreamMethods.EndReadId;
        }

        private bool HasOverriddenBeginEndWrite()
        {
            Func<byte[], int, int, AsyncCallback?, object?, IAsyncResult> begin = BeginWrite;
            Action<IAsyncResult> end = EndWrite;
            return begin.MethodId != StreamMethods.BeginWriteId || end.MethodId != StreamMethods.EndWriteId;
        }

        // Stream's own APM methods, as delegates bound non-virtually through base.
        private sealed class StreamMethods : Stream
        {
            internal static readonly int BeginReadId = new StreamMethods().BaseBeginRead();
            internal static readonly int EndReadId = new StreamMethods().BaseEndRead();
            internal static readonly int BeginWriteId = new StreamMethods().BaseBeginWrite();
            internal static readonly int EndWriteId = new StreamMethods().BaseEndWrite();

            private int BaseBeginRead() => ((Func<byte[], int, int, AsyncCallback?, object?, IAsyncResult>)base.BeginRead).MethodId;
            private int BaseEndRead() => ((Func<IAsyncResult, int>)base.EndRead).MethodId;
            private int BaseBeginWrite() => ((Func<byte[], int, int, AsyncCallback?, object?, IAsyncResult>)base.BeginWrite).MethodId;
            private int BaseEndWrite() => ((Action<IAsyncResult>)base.EndWrite).MethodId;

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
