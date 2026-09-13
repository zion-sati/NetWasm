// Adapted from dotnet/runtime System.Private.CoreLib runtime contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
namespace System
{
    public interface IAsyncResult
    {
        object? AsyncState { get; }
        bool CompletedSynchronously { get; }
        bool IsCompleted { get; }
    }

    public interface ICloneable
    {
        object Clone();
    }

    public interface IFormatProvider
    {
        object? GetFormat(Type? formatType);
    }

    public interface IFormattable
    {
        string ToString(string? format, IFormatProvider? formatProvider);
    }

    public interface ICustomFormatter
    {
        string? Format(string? format, object? arg, IFormatProvider? formatProvider);
    }

    public interface ISpanFormattable : IFormattable
    {
        bool TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format,
            IFormatProvider? provider);
    }

    // Portable UTF-8 formatting/parsing contracts from System.Private.CoreLib.
    // Implementations are opt-in; the interfaces themselves carry no culture
    // or host dependency.
    public interface IUtf8SpanFormattable
    {
        bool TryFormat(
            Span<byte> utf8Destination,
            out int bytesWritten,
            ReadOnlySpan<char> format,
            IFormatProvider? provider);
    }

    public interface IUtf8SpanParsable<TSelf>
        where TSelf : IUtf8SpanParsable<TSelf>?
    {
        static abstract TSelf Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider);

        static abstract bool TryParse(
            ReadOnlySpan<byte> utf8Text,
            IFormatProvider? provider,
            [Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TSelf result);
    }

    public class EventArgs
    {
        public EventArgs()
        {
        }

        public static readonly EventArgs Empty;

        static EventArgs()
        {
            Empty = new EventArgs();
        }
    }

    public delegate void AsyncCallback(IAsyncResult result);
}

namespace System.Runtime.CompilerServices
{
    public interface ITuple
    {
        int Length { get; }
        object? this[int index] { get; }
    }
}

namespace System.Threading
{
    public delegate void ContextCallback(object? state);
}
