// Ported from dotnet/runtime System.Private.CoreLib; upstream commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Built-in codecs retain the invariant profile. Custom providers supply additional codecs;
// normalization queries do not add Unicode normalization algorithms. Encoding.UTF7 returns the
// UTF7Encoding instance as upstream does; GetEncoding keeps upstream's UTF-7 lookup restriction.
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
    public static Encoding Latin1 { get => Latin1Encoding.s_default; }
    public static Encoding Default { get => DefaultEncoding; }
    [Obsolete(Obsoletions.SystemTextEncodingUTF7Message, DiagnosticId = Obsoletions.SystemTextEncodingUTF7DiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
    public static Encoding UTF7 => UTF7Encoding.s_default;
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
    public virtual int GetByteCount(string s) { ArgumentNullException.ThrowIfNull(s); return GetByteCount(s.AsSpan()); }
    public int GetByteCount(string s, int index, int count) { ArgumentNullException.ThrowIfNull(s); ValidateRange(index, count, s.Length); return GetByteCount(s.AsSpan(index, count)); }
    public abstract int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex);
    public unsafe virtual int GetBytes(char* chars, int charCount, byte* bytes, int byteCount) => GetBytes(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount));
    public virtual byte[] GetBytes(char[] chars) => GetBytes(chars, 0, chars?.Length ?? throw new ArgumentNullException());
    public virtual byte[] GetBytes(char[] chars, int index, int count) { var result = new byte[GetByteCount(chars, index, count)]; GetBytes(chars, index, count, result, 0); return result; }
    public virtual byte[] GetBytes(string s) { ArgumentNullException.ThrowIfNull(s); return GetBytes(s, 0, s.Length); }
    public byte[] GetBytes(string s, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(s);
        ValidateRange(index, count, s.Length);
        var source = s.AsSpan(index, count);
        var result = new byte[GetByteCount(source)];
        GetBytes(source, result);
        return result;
    }
    public virtual int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateRange(charIndex, charCount, s.Length);
        ValidateDestination(byteIndex, bytes.Length);
        return GetBytes(s.AsSpan(charIndex, charCount), bytes.AsSpan(byteIndex));
    }
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
    public virtual string GetString(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return GetString(new ReadOnlySpan<byte>(bytes));
    }
    public virtual string GetString(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateRange(index, count, bytes.Length);
        return GetString(new ReadOnlySpan<byte>(bytes, index, count));
    }
    public unsafe string GetString(byte* bytes, int byteCount) => GetString(new ReadOnlySpan<byte>(bytes, byteCount));
    public string GetString(ReadOnlySpan<byte> bytes)
    {
        var charCount = GetCharCount(bytes);
        if (charCount == 0) return string.Empty;
        var result = new string('\0', charCount);
        var destination = new Span<char>(ref result.GetRawStringData(), charCount);
        var written = GetChars(bytes, destination);
        return written == charCount ? result : throw new InvalidOperationException();
    }
    public virtual Decoder GetDecoder() { var decoder = new EncodingDecoder(this); decoder.Fallback = DecoderFallback; return decoder; }
    public virtual Encoder GetEncoder() { var encoder = new EncodingEncoder(this); encoder.Fallback = EncoderFallback; return encoder; }
    public virtual bool TryGetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten) { if (GetByteCount(chars) > bytes.Length) { bytesWritten = 0; return false; } bytesWritten = GetBytes(chars, bytes); return true; }
    public virtual bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars) { if (GetCharCount(bytes) > chars.Length) { return false; } GetChars(bytes, chars); return true; }
    public virtual bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten) { if (GetCharCount(bytes) > chars.Length) { charsWritten = 0; return false; } charsWritten = GetChars(bytes, chars); return true; }
    public virtual object Clone()
    {
        var clone = (Encoding)MemberwiseClone();
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
    public static void RegisterProvider(EncodingProvider provider) => EncodingProvider.AddProvider(provider);

    private static Encoding? FilterDisallowedEncodings(Encoding? encoding) =>
        encoding?.CodePage == 65000 ? null : encoding;

    public static Encoding GetEncoding(int codepage)
    {
        var supplied = FilterDisallowedEncodings(EncodingProvider.GetEncodingFromProvider(codepage));
        if (supplied is not null) return supplied;
        if (codepage < 0 || codepage > 65535) throw new ArgumentOutOfRangeException(nameof(codepage));
        return codepage switch
        {
            0 => Default,
            20127 => new ASCIIEncoding(),
            Latin1Encoding.Latin1CodePage => new Latin1Encoding(),
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
        var supplied = FilterDisallowedEncodings(EncodingProvider.GetEncodingFromProvider(codepage, encoderFallback, decoderFallback));
        if (supplied is not null) return supplied;
        var result = (Encoding)GetEncoding(codepage).Clone();
        result.EncoderFallback = encoderFallback;
        result.DecoderFallback = decoderFallback;
        return result;
    }

    public static Encoding GetEncoding(string name) =>
        FilterDisallowedEncodings(EncodingProvider.GetEncodingFromProvider(name)) ??
        GetEncoding(GetCodePageFromName(name));

    private static int GetCodePageFromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, "ascii", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "us-ascii", StringComparison.OrdinalIgnoreCase)) return 20127;
        if (name.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase) || name.Equals("latin1", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("iso8859-1", StringComparison.OrdinalIgnoreCase) || name.Equals("iso_8859-1", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("iso_8859-1:1987", StringComparison.OrdinalIgnoreCase) || name.Equals("l1", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("cp819", StringComparison.OrdinalIgnoreCase) || name.Equals("ibm819", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("csisolatin1", StringComparison.OrdinalIgnoreCase) || name.Equals("iso-ir-100", StringComparison.OrdinalIgnoreCase)) return Latin1Encoding.Latin1CodePage;
        if (string.Equals(name, "utf-8", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf8", StringComparison.OrdinalIgnoreCase)) return 65001;
        if (string.Equals(name, "utf-16", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "unicode", StringComparison.OrdinalIgnoreCase)) return 1200;
        if (string.Equals(name, "utf-16be", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf16be", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "unicodeFFFE", StringComparison.OrdinalIgnoreCase)) return 1201;
        if (string.Equals(name, "utf-32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf32", StringComparison.OrdinalIgnoreCase)) return 12000;
        if (string.Equals(name, "utf-32be", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "utf32be", StringComparison.OrdinalIgnoreCase)) return 12001;
        throw new NotSupportedException();
    }

    public static Encoding GetEncoding(string name, EncoderFallback encoderFallback, DecoderFallback decoderFallback) =>
        FilterDisallowedEncodings(EncodingProvider.GetEncodingFromProvider(name, encoderFallback, decoderFallback)) ??
        GetEncoding(GetCodePageFromName(name), encoderFallback, decoderFallback);

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

    public bool IsAlwaysNormalized() => IsAlwaysNormalized(NormalizationForm.FormC);

    public virtual bool IsAlwaysNormalized(NormalizationForm form) => false;

    public static EncodingInfo[] GetEncodings()
    {
        var builtins = new[] { new EncodingInfo(20127, "us-ascii"), new EncodingInfo(65001, "utf-8"), new EncodingInfo(1200, "utf-16"), new EncodingInfo(1201, "utf-16BE"), new EncodingInfo(12000, "utf-32"), new EncodingInfo(12001, "utf-32BE") };
        var supplied = EncodingProvider.GetEncodingListFromProviders();
        if (supplied is null) return builtins;
        foreach (var info in builtins) supplied.TryAdd(info.CodePage, info);
        supplied.Remove(65000);
        var result = new EncodingInfo[supplied.Count];
        supplied.Values.CopyTo(result, 0);
        return result;
    }
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
