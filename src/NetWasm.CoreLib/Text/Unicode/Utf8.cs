// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
using System.Text;
using System.Runtime.CompilerServices;

namespace System.Text.Unicode;

public static class Utf8
{
    public static bool IsValid(ReadOnlySpan<byte> value) => IndexOfInvalidSubsequence(value) < 0;
    public static int IndexOfInvalidSubsequence(ReadOnlySpan<byte> value)
    {
        for (var index = 0; index < value.Length;) { var status = Rune.DecodeFromUtf8(value.Slice(index), out _, out var consumed); if (status == System.Buffers.OperationStatus.Done) index += consumed; else return index; }
        return -1;
    }
    public static System.Buffers.OperationStatus FromUtf16(ReadOnlySpan<char> source, Span<byte> destination, out int charsRead, out int bytesWritten, bool replaceInvalidSequences = true, bool isFinalBlock = true)
    {
        charsRead = 0; bytesWritten = 0;
        while (charsRead < source.Length)
        {
            var status = Rune.DecodeFromUtf16(source.Slice(charsRead), out var rune, out var consumed);
            if (status == System.Buffers.OperationStatus.NeedMoreData && !isFinalBlock) return System.Buffers.OperationStatus.NeedMoreData;
            if (status == System.Buffers.OperationStatus.InvalidData) { if (!replaceInvalidSequences) return System.Buffers.OperationStatus.InvalidData; rune = Rune.ReplacementChar; consumed = consumed == 0 ? 1 : consumed; }
            if (destination.Length - bytesWritten < rune.Utf8SequenceLength) return System.Buffers.OperationStatus.DestinationTooSmall;
            rune.EncodeToUtf8(destination.Slice(bytesWritten)); charsRead += consumed; bytesWritten += rune.Utf8SequenceLength;
        }
        return System.Buffers.OperationStatus.Done;
    }
    public static System.Buffers.OperationStatus ToUtf16(ReadOnlySpan<byte> source, Span<char> destination, out int bytesRead, out int charsWritten, bool replaceInvalidSequences = true, bool isFinalBlock = true)
    {
        bytesRead = 0; charsWritten = 0;
        while (bytesRead < source.Length)
        {
            var status = Rune.DecodeFromUtf8(source.Slice(bytesRead), out var rune, out var consumed);
            if (status == System.Buffers.OperationStatus.NeedMoreData && !isFinalBlock) return System.Buffers.OperationStatus.NeedMoreData;
            if (status != System.Buffers.OperationStatus.Done) { if (!replaceInvalidSequences) return System.Buffers.OperationStatus.InvalidData; rune = Rune.ReplacementChar; consumed = consumed == 0 ? 1 : consumed; }
            if (destination.Length - charsWritten < rune.Utf16SequenceLength) return System.Buffers.OperationStatus.DestinationTooSmall;
            rune.EncodeToUtf16(destination.Slice(charsWritten)); bytesRead += consumed; charsWritten += rune.Utf16SequenceLength;
        }
        return System.Buffers.OperationStatus.Done;
    }

    public static bool TryWrite(
        Span<byte> destination,
        [InterpolatedStringHandlerArgument(nameof(destination))]
        ref TryWriteInterpolatedStringHandler handler,
        out int bytesWritten) => CompleteTryWrite(ref handler, out bytesWritten);

    public static bool TryWrite(
        Span<byte> destination,
        IFormatProvider? provider,
        [InterpolatedStringHandlerArgument(nameof(destination), nameof(provider))]
        ref TryWriteInterpolatedStringHandler handler,
        out int bytesWritten) => CompleteTryWrite(ref handler, out bytesWritten);

    [InterpolatedStringHandler]
    public ref struct TryWriteInterpolatedStringHandler
    {
        private Span<byte> _destination;
        private IFormatProvider? _provider;
        private int _index;
        private bool _success;

        public TryWriteInterpolatedStringHandler(int literalLength, int formattedCount, Span<byte> destination, out bool shouldAppend)
            : this(literalLength, formattedCount, destination, null, out shouldAppend)
        {
        }

        public TryWriteInterpolatedStringHandler(int literalLength, int formattedCount, Span<byte> destination, IFormatProvider? provider, out bool shouldAppend)
        {
            _destination = destination;
            _provider = provider;
            _index = 0;
            _success = shouldAppend = destination.Length >= literalLength;
        }

        internal int Written => _index;
        internal bool Success => _success;

        public bool AppendLiteral(string value) => AppendFormatted(value);
        public bool AppendFormatted<T>(T value) => AppendFormatted(value, 0, null);
        public bool AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);
        public bool AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);
        public bool AppendFormatted<T>(T value, int alignment, string? format) =>
            AppendAligned(StringBuilder.FormatValue(value, format, _provider), alignment);
        public bool AppendFormatted(string? value) => AppendAligned(value ?? string.Empty, 0);
        public bool AppendFormatted(string? value, int alignment = 0, string? format = null) =>
            AppendAligned(StringBuilder.FormatValue(value, format, _provider), alignment);
        public bool AppendFormatted(object? value, int alignment = 0, string? format = null) =>
            AppendAligned(StringBuilder.FormatValue(value, format, _provider), alignment);
        public bool AppendFormatted(scoped ReadOnlySpan<char> value) => AppendAligned(value, 0);
        public bool AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null) => AppendAligned(value, alignment);
        public bool AppendFormatted(scoped ReadOnlySpan<byte> value) => AppendBytes(value);
        public bool AppendFormatted(scoped ReadOnlySpan<byte> value, int alignment = 0, string? format = null) => AppendBytes(value);

        private bool AppendAligned(string value, int alignment)
        {
            var padding = Math.Max(0, (alignment < 0 ? -alignment : alignment) - value.Length);
            if (alignment > 0 && !AppendRepeated((byte)' ', padding)) return false;
            if (!AppendAligned(value.AsSpan(), 0)) return false;
            return alignment >= 0 || AppendRepeated((byte)' ', padding);
        }

        private bool AppendAligned(scoped ReadOnlySpan<char> value, int alignment)
        {
            var padding = Math.Max(0, (alignment < 0 ? -alignment : alignment) - value.Length);
            if (alignment > 0 && !AppendRepeated((byte)' ', padding)) return false;
            var encoded = Encoding.UTF8.GetBytes(value.ToArray());
            if (!AppendBytes(encoded)) return false;
            return alignment >= 0 || AppendRepeated((byte)' ', padding);
        }

        private bool AppendBytes(scoped ReadOnlySpan<byte> value)
        {
            if (!_success) return false;
            if (value.Length > _destination.Length - _index) { _success = false; return false; }
            for (var offset = 0; offset < value.Length; offset++) _destination[_index++] = value[offset];
            return true;
        }

        private bool AppendRepeated(byte value, int count)
        {
            if (count == 0) return _success;
            if (count > _destination.Length - _index) { _success = false; return false; }
            for (var offset = 0; offset < count; offset++) _destination[_index++] = value;
            return true;
        }
    }

    private static bool CompleteTryWrite(ref TryWriteInterpolatedStringHandler handler, out int bytesWritten)
    {
        bytesWritten = handler.Success ? handler.Written : 0;
        return handler.Success;
    }
}
