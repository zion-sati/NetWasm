// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// The implementation intentionally retains only invariant ordinal codecs; culture, code pages,
// providers, and normalization are outside the NetWasm profile.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public abstract class Encoding : ICloneable
{
    private static readonly UTF8Encoding DefaultEncoding = ReadOnly(new UTF8Encoding(false));
    private EncoderFallback _encoderFallback = EncoderFallback.ReplacementFallback;
    private DecoderFallback _decoderFallback = DecoderFallback.ReplacementFallback;
    private readonly int _codePage;
    private bool _isReadOnly = true;
    protected Encoding() { }
    protected Encoding(int codePage)
    {
        if (codePage < 0) throw new ArgumentOutOfRangeException(nameof(codePage));
        _codePage = codePage;
    }
    protected Encoding(int codePage, EncoderFallback? encoderFallback, DecoderFallback? decoderFallback)
    {
        if (codePage < 0) throw new ArgumentOutOfRangeException(nameof(codePage));
        _codePage = codePage;
        _encoderFallback = encoderFallback ?? EncoderFallback.ReplacementFallback;
        _decoderFallback = decoderFallback ?? DecoderFallback.ReplacementFallback;
    }
    public static Encoding ASCII { get => ASCIIEncoding.Default; }
    public static Encoding UTF8 { get => UTF8Encoding.Default; }
    public static Encoding Unicode { get => UnicodeEncoding.LittleEndianDefault; }
    public static Encoding BigEndianUnicode { get => UnicodeEncoding.BigEndianDefault; }
    public static Encoding UTF32 { get => UTF32Encoding.Default; }
    public static Encoding Latin1 { get => throw new PlatformNotSupportedException(); }
    public static Encoding Default { get => DefaultEncoding; }
    public static Encoding UTF7 => throw new PlatformNotSupportedException("UTF-7 is not supported by the invariant profile.");
    public EncoderFallback EncoderFallback { get => _encoderFallback; set { if (_isReadOnly) throw new InvalidOperationException(); ArgumentNullException.ThrowIfNull(value); _encoderFallback = value; } }
    public DecoderFallback DecoderFallback { get => _decoderFallback; set { if (_isReadOnly) throw new InvalidOperationException(); ArgumentNullException.ThrowIfNull(value); _decoderFallback = value; } }
    public bool IsReadOnly { get => _isReadOnly; }
    protected void MakeReadOnly() => _isReadOnly = true;
    internal static T ReadOnly<T>(T encoding) where T : Encoding { encoding.MakeReadOnly(); return encoding; }
    public virtual int CodePage { get => _codePage; }
    public virtual string EncodingName
    {
        get => CodePage switch
        {
            1200 => "Unicode (UTF-16)",
            1201 => "Unicode (Big-Endian)",
            12000 => "Unicode (UTF-32)",
            12001 => "Unicode (UTF-32 Big-Endian)",
            20127 => "US-ASCII",
            65001 => "Unicode (UTF-8)",
            _ => "Unicode Encoding",
        };
    }
    public virtual string WebName { get => EncodingName; }
    public virtual string HeaderName { get => WebName; }
    public virtual string BodyName { get => WebName; }
    public virtual int WindowsCodePage { get => CodePage; }
    public virtual bool IsSingleByte { get => false; }
    public virtual bool IsBrowserDisplay { get => false; }
    public virtual bool IsBrowserSave { get => false; }
    public virtual bool IsMailNewsDisplay { get => false; }
    public virtual bool IsMailNewsSave { get => false; }
    public virtual ReadOnlySpan<byte> Preamble { get => ReadOnlySpan<byte>.Empty; }
    public virtual byte[] GetPreamble() => Preamble.ToArray();

    public abstract int GetByteCount(char[] chars, int index, int count);
    public unsafe virtual int GetByteCount(char* chars, int count) => GetByteCount(new ReadOnlySpan<char>(chars, count));
    public abstract int GetMaxByteCount(int charCount);
    public abstract int GetMaxCharCount(int byteCount);
    public virtual int GetByteCount(char[] chars) => GetByteCount(chars, 0, chars?.Length ?? throw new ArgumentNullException());
    public virtual int GetByteCount(ReadOnlySpan<char> chars) => GetByteCount(chars.ToArray(), 0, chars.Length);
    public virtual int GetByteCount(string s) { ArgumentNullException.ThrowIfNull(s); return GetByteCount(s.ToCharArray(), 0, s.Length); }
    public int GetByteCount(string s, int index, int count) { ArgumentNullException.ThrowIfNull(s); ValidateRange(index, count, s.Length); return GetByteCount(s.ToCharArray(), index, count); }
    public abstract int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex);
    public unsafe virtual int GetBytes(char* chars, int charCount, byte* bytes, int byteCount) => GetBytes(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount));
    public virtual byte[] GetBytes(char[] chars) => GetBytes(chars, 0, chars?.Length ?? throw new ArgumentNullException());
    public virtual byte[] GetBytes(char[] chars, int index, int count) { var result = new byte[GetByteCount(chars, index, count)]; GetBytes(chars, index, count, result, 0); return result; }
    public virtual byte[] GetBytes(string s) { ArgumentNullException.ThrowIfNull(s); return GetBytes(s, 0, s.Length); }
    public byte[] GetBytes(string s, int index, int count) { ArgumentNullException.ThrowIfNull(s); ValidateRange(index, count, s.Length); return GetBytes(s.ToCharArray(), index, count); }
    public virtual int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex) { ArgumentNullException.ThrowIfNull(s); return GetBytes(s.ToCharArray(), charIndex, charCount, bytes, byteIndex); }
    public virtual int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        var required = GetByteCount(chars);
        if (required > bytes.Length) throw new ArgumentException("The destination buffer is too small.", nameof(bytes));
        var result = GetBytes(chars.ToArray());
        for (var index = 0; index < result.Length; index++) bytes[index] = result[index];
        return result.Length;
    }
    public abstract int GetCharCount(byte[] bytes, int index, int count);
    public unsafe virtual int GetCharCount(byte* bytes, int count) => GetCharCount(new ReadOnlySpan<byte>(bytes, count));
    public virtual int GetCharCount(byte[] bytes) => GetCharCount(bytes, 0, bytes?.Length ?? throw new ArgumentNullException());
    public virtual int GetCharCount(ReadOnlySpan<byte> bytes) => GetCharCount(bytes.ToArray(), 0, bytes.Length);
    public abstract int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex);
    public unsafe virtual int GetChars(byte* bytes, int byteCount, char* chars, int charCount) => GetChars(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount));
    public virtual char[] GetChars(byte[] bytes) => GetChars(bytes, 0, bytes?.Length ?? throw new ArgumentNullException());
    public virtual char[] GetChars(byte[] bytes, int index, int count) { var result = new char[GetCharCount(bytes, index, count)]; GetChars(bytes, index, count, result, 0); return result; }
    public virtual int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        var result = GetChars(bytes.ToArray());
        if (result.Length > chars.Length) throw new ArgumentException("The destination buffer is too small.", nameof(chars));
        for (var index = 0; index < result.Length; index++) chars[index] = result[index];
        return result.Length;
    }
    public virtual string GetString(byte[] bytes) => CreateString(GetChars(bytes));
    public virtual string GetString(byte[] bytes, int index, int count) => CreateString(GetChars(bytes, index, count));
    public unsafe string GetString(byte* bytes, int byteCount) => CreateString(GetChars(new ReadOnlySpan<byte>(bytes, byteCount).ToArray()));
    public string GetString(ReadOnlySpan<byte> bytes) => CreateString(GetChars(bytes.ToArray()));
    public virtual Decoder GetDecoder() { var decoder = new EncodingDecoder(this); decoder.Fallback = DecoderFallback; return decoder; }
    public virtual Encoder GetEncoder() { var encoder = new EncodingEncoder(this); encoder.Fallback = EncoderFallback; return encoder; }
    public virtual bool TryGetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten) { if (GetByteCount(chars) > bytes.Length) { bytesWritten = 0; return false; } bytesWritten = GetBytes(chars, bytes); return true; }
    public virtual bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars) { if (GetCharCount(bytes) > chars.Length) { return false; } GetChars(bytes, chars); return true; }
    public virtual bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten) { if (GetCharCount(bytes) > chars.Length) { charsWritten = 0; return false; } charsWritten = GetChars(bytes, chars); return true; }
    public virtual object Clone()
    {
        // The framework implementation uses MemberwiseClone. CoreLib intentionally
        // keeps that runtime primitive out of the profile, so clone the supported
        // invariant codecs from their public constructor state instead.
        var clone = CodePage switch
        {
            20127 => new ASCIIEncoding(),
            65001 => new UTF8Encoding(Preamble.Length != 0),
            1200 => new UnicodeEncoding(false, Preamble.Length != 0),
            1201 => new UnicodeEncoding(true, Preamble.Length != 0),
            12000 => new UTF32Encoding(false, Preamble.Length != 0),
            12001 => new UTF32Encoding(true, Preamble.Length != 0),
            _ => this
        };

        if (ReferenceEquals(clone, this)) return this;
        clone._encoderFallback = _encoderFallback;
        clone._decoderFallback = _decoderFallback;
        clone._isReadOnly = false;
        return clone;
    }
    public override bool Equals(object? value) =>
        value is Encoding other &&
        CodePage == other.CodePage &&
        EncoderFallback.Equals(other.EncoderFallback) &&
        DecoderFallback.Equals(other.DecoderFallback);
    public override int GetHashCode() =>
        HashCode.Combine(CodePage, EncoderFallback, DecoderFallback);
    public static Encoding GetEncoding(int codepage)
    {
        if (codepage < 0 || codepage > 65535) throw new ArgumentOutOfRangeException(nameof(codepage));
        return codepage switch
        {
            0 => Default,
            20127 => new ASCIIEncoding(),
            65001 => new UTF8Encoding(),
            1200 => new UnicodeEncoding(false, false),
            1201 => new UnicodeEncoding(true, false),
            12000 => new UTF32Encoding(false, false),
            12001 => new UTF32Encoding(true, false),
            _ => throw new NotSupportedException()
        };
    }

    public static Encoding GetEncoding(int codepage, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
    {
        ArgumentNullException.ThrowIfNull(encoderFallback);
        ArgumentNullException.ThrowIfNull(decoderFallback);
        var result = (Encoding)GetEncoding(codepage).Clone();
        result.EncoderFallback = encoderFallback;
        result.DecoderFallback = decoderFallback;
        return result;
    }

    public static Encoding GetEncoding(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name switch
        {
            "ascii" or "ASCII" or "us-ascii" or "US-ASCII" => GetEncoding(20127),
            "utf-8" or "UTF-8" or "utf8" or "UTF8" => GetEncoding(65001),
            "utf-16" or "UTF-16" or "unicode" or "Unicode" => GetEncoding(1200),
            "utf-16be" or "UTF-16BE" or "utf16be" or "UTF16BE" or "unicodeFFFE" => GetEncoding(1201),
            "utf-32" or "UTF-32" or "utf32" or "UTF32" => GetEncoding(12000),
            "utf-32be" or "UTF-32BE" or "utf32be" or "UTF32BE" => new UTF32Encoding(true, false),
            _ => throw new NotSupportedException()
        };
    }

    public static Encoding GetEncoding(string name, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(encoderFallback);
        ArgumentNullException.ThrowIfNull(decoderFallback);
        var result = GetEncoding(name).Clone() as Encoding ?? throw new InvalidOperationException();
        result.EncoderFallback = encoderFallback;
        result.DecoderFallback = decoderFallback;
        return result;
    }
    public static byte[] Convert(Encoding srcEncoding, Encoding dstEncoding, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert(srcEncoding, dstEncoding, bytes, 0, bytes.Length);
    }

    public static byte[] Convert(Encoding srcEncoding, Encoding dstEncoding, byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(srcEncoding);
        ArgumentNullException.ThrowIfNull(dstEncoding);
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateRange(index, count, bytes.Length);
        return dstEncoding.GetBytes(srcEncoding.GetChars(bytes, index, count));
    }

    public bool IsAlwaysNormalized() => false;

    public static EncodingInfo[] GetEncodings() => new[] { new EncodingInfo(20127, "us-ascii"), new EncodingInfo(65001, "utf-8"), new EncodingInfo(1200, "utf-16"), new EncodingInfo(1201, "utf-16BE"), new EncodingInfo(12000, "utf-32"), new EncodingInfo(12001, "utf-32BE") };
    internal string EncoderReplacementString => EncoderFallback is EncoderReplacementFallback fallback
        ? fallback.DefaultString
        : throw new EncoderFallbackException();
    internal string DecoderReplacementString => DecoderFallback is DecoderReplacementFallback fallback
        ? fallback.DefaultString
        : throw new DecoderFallbackException();
    internal bool UsesEncoderExceptionFallback => EncoderFallback is EncoderExceptionFallback;
    internal bool UsesDecoderExceptionFallback => DecoderFallback is DecoderExceptionFallback;
    internal static void ValidateRange(int index, int count, int length)
    {
        if (index < 0 || count < 0 || index > length - count) throw new ArgumentOutOfRangeException();
    }

    internal static void ValidateDestination(int index, int length)
    {
        if (index < 0 || index > length) throw new ArgumentOutOfRangeException();
    }

    internal static void EnsureDestination(int index, int required, int length)
    {
        ValidateDestination(index, length);
        if (required > length - index) throw new ArgumentException("The destination buffer is too small.");
    }
    private static string CreateString(char[] chars) { var result = String.Empty; for (var index = 0; index < chars.Length; index++) result = String.Concat(result, new string(chars[index], 1)); return result; }
}

public sealed class EncodingInfo
{
    private readonly EncodingProvider? _provider;
    private readonly int _codePage;
    private readonly string _name;
    private readonly string _displayName;
    public EncodingInfo(EncodingProvider provider, int codePage, string name, string displayName)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(displayName);
        _provider = provider;
        _codePage = codePage;
        _name = name;
        _displayName = displayName;
    }
    internal EncodingInfo(int codePage, string name)
    {
        _provider = null;
        _codePage = codePage;
        _name = name;
        _displayName = name;
    }
    public int CodePage { get => _codePage; }
    public string Name { get => _name; }
    public string DisplayName { get => _displayName; }
    public Encoding GetEncoding() => _provider?.GetEncoding(_codePage) ?? Encoding.GetEncoding(_codePage);
    public override bool Equals(object? value) => value is EncodingInfo other && _codePage == other._codePage;
    public override int GetHashCode() => _codePage;
}

public abstract class EncodingProvider
{
    protected EncodingProvider() { }
    public abstract Encoding? GetEncoding(int codepage);
    public virtual Encoding? GetEncoding(int codepage, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
    {
        ArgumentNullException.ThrowIfNull(encoderFallback);
        ArgumentNullException.ThrowIfNull(decoderFallback);
        var encoding = GetEncoding(codepage);
        if (encoding is null) return null;
        var clone = (Encoding)encoding.Clone();
        clone.EncoderFallback = encoderFallback;
        clone.DecoderFallback = decoderFallback;
        return clone;
    }
    public abstract Encoding? GetEncoding(string name);
    public virtual Encoding? GetEncoding(string name, EncoderFallback encoderFallback, DecoderFallback decoderFallback)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(encoderFallback);
        ArgumentNullException.ThrowIfNull(decoderFallback);
        var encoding = GetEncoding(name);
        if (encoding is null) return null;
        var clone = (Encoding)encoding.Clone();
        clone.EncoderFallback = encoderFallback;
        clone.DecoderFallback = decoderFallback;
        return clone;
    }
    public virtual Collections.Generic.IEnumerable<EncodingInfo> GetEncodings() => [];
}
