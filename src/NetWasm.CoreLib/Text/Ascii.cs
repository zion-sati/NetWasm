// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public static class Ascii
{
    public static bool IsValid(byte value) => value <= 0x7F;
    public static bool IsValid(char value) => value <= 0x7F;
    public static bool IsValid(ReadOnlySpan<byte> value) { for (var index = 0; index < value.Length; index++) if (value[index] > 0x7F) return false; return true; }
    public static bool IsValid(ReadOnlySpan<char> value) { for (var index = 0; index < value.Length; index++) if (value[index] > 0x7F) return false; return true; }
    public static bool Equals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => Equal(left, right, false);
    public static bool Equals(ReadOnlySpan<byte> left, ReadOnlySpan<char> right) { if (left.Length != right.Length) return false; for (var index = 0; index < left.Length; index++) if (left[index] > 0x7F || right[index] > 0x7F || left[index] != right[index]) return false; return true; }
    public static bool Equals(ReadOnlySpan<char> left, ReadOnlySpan<byte> right) => Equals(right, left);
    public static bool Equals(ReadOnlySpan<char> left, ReadOnlySpan<char> right) { if (left.Length != right.Length) return false; for (var index = 0; index < left.Length; index++) if (left[index] > 0x7F || right[index] > 0x7F || left[index] != right[index]) return false; return true; }
    public static bool EqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => Equal(left, right, true);
    public static bool EqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<char> right) { if (left.Length != right.Length) return false; for (var index = 0; index < left.Length; index++) if (left[index] > 0x7F || right[index] > 0x7F || Fold(left[index]) != Fold((byte)right[index])) return false; return true; }
    public static bool EqualsIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<byte> right) => EqualsIgnoreCase(right, left);
    public static bool EqualsIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<char> right) { if (left.Length != right.Length) return false; for (var index = 0; index < left.Length; index++) if (left[index] > 0x7F || right[index] > 0x7F || Fold((byte)left[index]) != Fold((byte)right[index])) return false; return true; }

    public static System.Buffers.OperationStatus FromUtf16(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten)
    { var count = source.Length < destination.Length ? source.Length : destination.Length; for (var index = 0; index < count; index++) { if (source[index] > 0x7F) { bytesWritten = index; return System.Buffers.OperationStatus.InvalidData; } destination[index] = (byte)source[index]; } bytesWritten = count; return count == source.Length ? System.Buffers.OperationStatus.Done : System.Buffers.OperationStatus.DestinationTooSmall; }
    public static System.Buffers.OperationStatus ToUtf16(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
    { var count = source.Length < destination.Length ? source.Length : destination.Length; for (var index = 0; index < count; index++) destination[index] = (char)source[index]; charsWritten = count; return count == source.Length ? System.Buffers.OperationStatus.Done : System.Buffers.OperationStatus.DestinationTooSmall; }
    public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) => Transform(source, destination, out bytesWritten, false);
    public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) => Transform(source, destination, out bytesWritten, true);
    public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten) => Transform(source, destination, out charsWritten, false);
    public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten) => Transform(source, destination, out charsWritten, true);
    public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten) { var status = ToUtf16(source, destination, out charsWritten); for (var index = 0; index < charsWritten; index++) destination[index] = Lower(destination[index]); return status; }
    public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten) { var status = ToUtf16(source, destination, out charsWritten); for (var index = 0; index < charsWritten; index++) destination[index] = Upper(destination[index]); return status; }
    public static System.Buffers.OperationStatus ToLower(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten) { var status = FromUtf16(source, destination, out bytesWritten); for (var index = 0; index < bytesWritten; index++) destination[index] = FoldLower(destination[index]); return status; }
    public static System.Buffers.OperationStatus ToUpper(ReadOnlySpan<char> source, Span<byte> destination, out int bytesWritten) { var status = FromUtf16(source, destination, out bytesWritten); for (var index = 0; index < bytesWritten; index++) destination[index] = FoldUpper(destination[index]); return status; }
    public static System.Buffers.OperationStatus ToLowerInPlace(Span<byte> value, out int bytesWritten) => TransformInPlace(value, out bytesWritten, false);
    public static System.Buffers.OperationStatus ToUpperInPlace(Span<byte> value, out int bytesWritten) => TransformInPlace(value, out bytesWritten, true);
    public static System.Buffers.OperationStatus ToLowerInPlace(Span<char> value, out int charsWritten) => TransformInPlace(value, out charsWritten, false);
    public static System.Buffers.OperationStatus ToUpperInPlace(Span<char> value, out int charsWritten) => TransformInPlace(value, out charsWritten, true);
    public static Range Trim(ReadOnlySpan<byte> value) => TrimCore(value, 0);
    public static Range TrimStart(ReadOnlySpan<byte> value) => TrimCore(value, 1);
    public static Range TrimEnd(ReadOnlySpan<byte> value) => TrimCore(value, 2);
    public static Range Trim(ReadOnlySpan<char> value) => TrimCore(value, 0);
    public static Range TrimStart(ReadOnlySpan<char> value) => TrimCore(value, 1);
    public static Range TrimEnd(ReadOnlySpan<char> value) => TrimCore(value, 2);

    private static bool Equal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, bool ignoreCase) { if (left.Length != right.Length) return false; for (var index = 0; index < left.Length; index++) if (left[index] > 0x7F || right[index] > 0x7F || (ignoreCase ? Fold(left[index]) != Fold(right[index]) : left[index] != right[index])) return false; return true; }
    private static byte Fold(byte value) => value >= (byte)'a' && value <= (byte)'z' ? (byte)(value - 32) : value;
    private static byte FoldLower(byte value) => value >= (byte)'A' && value <= (byte)'Z' ? (byte)(value + 32) : value;
    private static byte FoldUpper(byte value) => Fold(value);
    private static char Lower(char value) => value >= 'A' && value <= 'Z' ? (char)(value + 32) : value;
    private static char Upper(char value) => value >= 'a' && value <= 'z' ? (char)(value - 32) : value;
    private static System.Buffers.OperationStatus Transform(ReadOnlySpan<byte> source, Span<byte> destination, out int written, bool upper) { var count = source.Length < destination.Length ? source.Length : destination.Length; for (var index = 0; index < count; index++) { if (source[index] > 0x7F) { written = index; return System.Buffers.OperationStatus.InvalidData; } destination[index] = upper ? FoldUpper(source[index]) : FoldLower(source[index]); } written = count; return count == source.Length ? System.Buffers.OperationStatus.Done : System.Buffers.OperationStatus.DestinationTooSmall; }
    private static System.Buffers.OperationStatus Transform(ReadOnlySpan<char> source, Span<char> destination, out int written, bool upper) { var count = source.Length < destination.Length ? source.Length : destination.Length; for (var index = 0; index < count; index++) { if (source[index] > 0x7F) { written = index; return System.Buffers.OperationStatus.InvalidData; } destination[index] = upper ? Upper(source[index]) : Lower(source[index]); } written = count; return count == source.Length ? System.Buffers.OperationStatus.Done : System.Buffers.OperationStatus.DestinationTooSmall; }
    private static System.Buffers.OperationStatus TransformInPlace(Span<byte> value, out int written, bool upper) => Transform(value, value, out written, upper);
    private static System.Buffers.OperationStatus TransformInPlace(Span<char> value, out int written, bool upper) => Transform(value, value, out written, upper);
    private static bool IsWhitespace(byte value) => value == 9 || value == 10 || value == 11 || value == 12 || value == 13 || value == 32;
    private static bool IsWhitespace(char value) => IsWhitespace((byte)value) && value <= 0x7F;
    private static Range TrimCore(ReadOnlySpan<byte> value, int mode) { var start = 0; var end = value.Length; if (mode != 2) while (start < end && IsWhitespace(value[start])) start++; if (mode != 1) while (end > start && IsWhitespace(value[end - 1])) end--; return new Range(start, end); }
    private static Range TrimCore(ReadOnlySpan<char> value, int mode) { var start = 0; var end = value.Length; if (mode != 2) while (start < end && IsWhitespace(value[start])) start++; if (mode != 1) while (end > start && IsWhitespace(value[end - 1])) end--; return new Range(start, end); }
}
