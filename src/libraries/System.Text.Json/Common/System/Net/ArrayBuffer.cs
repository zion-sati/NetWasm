// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Net
{
    // Warning: Mutable struct. This is the upstream sliding-buffer helper.
    [StructLayout(LayoutKind.Auto)]
    internal struct ArrayBuffer : IDisposable
    {
        private static int ArrayMaxLength => Array.MaxLength;

        private readonly bool _usePool;
        private byte[] _bytes;
        private int _activeStart;
        private int _availableStart;

        public ArrayBuffer(int initialSize, bool usePool = false)
        {
            Debug.Assert(initialSize > 0 || usePool);
            _usePool = usePool;
            _bytes = initialSize == 0
                ? Array.Empty<byte>()
                : usePool ? ArrayPool<byte>.Shared.Rent(initialSize) : new byte[initialSize];
            _activeStart = 0;
            _availableStart = 0;
        }

        public ArrayBuffer(byte[] buffer)
        {
            Debug.Assert(buffer.Length > 0);
            _usePool = false;
            _bytes = buffer;
            _activeStart = 0;
            _availableStart = 0;
        }

        public void Dispose()
        {
            _activeStart = 0;
            _availableStart = 0;
            byte[] array = _bytes;
            _bytes = null!;
            if (array is not null)
            {
                ReturnBufferIfPooled(array);
            }
        }

        public void ClearAndReturnBuffer()
        {
            Debug.Assert(_usePool);
            Debug.Assert(_bytes is not null);
            _activeStart = 0;
            _availableStart = 0;
            byte[] bufferToReturn = _bytes;
            _bytes = Array.Empty<byte>();
            ReturnBufferIfPooled(bufferToReturn);
        }

        public int ActiveLength => _availableStart - _activeStart;
        public Span<byte> ActiveSpan => new(_bytes, _activeStart, ActiveLength);
        public ReadOnlySpan<byte> ActiveReadOnlySpan => new(_bytes, _activeStart, ActiveLength);
        public Memory<byte> ActiveMemory => new(_bytes, _activeStart, ActiveLength);
        public int AvailableLength => _bytes.Length - _availableStart;
        public Span<byte> AvailableSpan => _bytes.AsSpan(_availableStart);
        public Memory<byte> AvailableMemory => _bytes.AsMemory(_availableStart);
        public Memory<byte> AvailableMemorySliced(int length) => new(_bytes, _availableStart, length);
        public int Capacity => _bytes.Length;
        public int ActiveStartOffset => _activeStart;
        public byte[] DangerousGetUnderlyingBuffer() => _bytes;

        public void Discard(int byteCount)
        {
            Debug.Assert(byteCount <= ActiveLength, $"Expected {byteCount} <= {ActiveLength}");
            _activeStart += byteCount;
            if (_activeStart == _availableStart)
            {
                _activeStart = 0;
                _availableStart = 0;
            }
        }

        public void Commit(int byteCount)
        {
            Debug.Assert(byteCount <= AvailableLength);
            _availableStart += byteCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureAvailableSpace(int byteCount)
        {
            if (byteCount > AvailableLength)
            {
                EnsureAvailableSpaceCore(byteCount);
            }
        }

        private void EnsureAvailableSpaceCore(int byteCount)
        {
            Debug.Assert(AvailableLength < byteCount);
            if (_bytes.Length == 0)
            {
                Debug.Assert(_usePool && _activeStart == 0 && _availableStart == 0);
                _bytes = ArrayPool<byte>.Shared.Rent(byteCount);
                return;
            }

            int totalFree = _activeStart + AvailableLength;
            if (byteCount <= totalFree)
            {
                Buffer.BlockCopy(_bytes, _activeStart, _bytes, 0, ActiveLength);
                _availableStart = ActiveLength;
                _activeStart = 0;
                Debug.Assert(byteCount <= AvailableLength);
                return;
            }

            int desiredSize = ActiveLength + byteCount;
            if ((uint)desiredSize > ArrayMaxLength)
            {
                throw new OutOfMemoryException();
            }

            int newSize = Math.Max(
                desiredSize,
                (int)Math.Min(ArrayMaxLength, 2 * (uint)_bytes.Length));
            byte[] newBytes = _usePool
                ? ArrayPool<byte>.Shared.Rent(newSize)
                : new byte[newSize];
            byte[] oldBytes = _bytes;
            if (ActiveLength != 0)
            {
                Buffer.BlockCopy(oldBytes, _activeStart, newBytes, 0, ActiveLength);
            }

            _availableStart = ActiveLength;
            _activeStart = 0;
            _bytes = newBytes;
            ReturnBufferIfPooled(oldBytes);
            Debug.Assert(byteCount <= AvailableLength);
        }

        public void Grow() => EnsureAvailableSpaceCore(AvailableLength + 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReturnBufferIfPooled(byte[] buffer)
        {
            if (_usePool && buffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
