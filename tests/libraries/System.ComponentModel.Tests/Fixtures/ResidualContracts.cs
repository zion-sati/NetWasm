// From the compiler harness's ResidualContractsCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0001, SYSLIB0050, SYSLIB0051
using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Authentication;
using System.Security.Authentication.ExtendedProtection;
using System.Text;
using System.Windows.Input;
namespace ResidualContractsFixture;

public sealed class Command : ICommand
{
    public int Executed;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => parameter is string;
    public void Execute(object? parameter) => Executed += parameter is string text ? text.Length : 100;
    public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public interface IGreeter
{
    string Greet(string name);
}

public class EchoProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => "hi " + args?[0];
    public object? Call(object? argument) => Invoke(null, new[] { argument });
}

public sealed class SerializingUri : Uri
{
    public SerializingUri(string text) : base(text) { }
    public void Fill(SerializationInfo info) => GetObjectData(info, default);
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);

    public static int Run(int input)
    {
        // UTF-7 encodes direct, optional and shifted characters as RFC 2152 describes.
        Encoding utf7 = Encoding.UTF7;
        if (utf7.CodePage != 65000 || utf7.WebName != "utf-7" || utf7.EncodingName != "Unicode (UTF-7)" || !utf7.IsReadOnly) return 1;
        if (!ReferenceEquals(utf7, Encoding.UTF7) || !utf7.Equals(new UTF7Encoding()) || utf7.Equals(new UTF7Encoding(true))) return 2;
        if (utf7.GetHashCode() != new UTF7Encoding().GetHashCode() || utf7.GetPreamble().Length != 0) return 3;
        string text = "Hi Mom -☺-!";
        if (Ascii(utf7.GetBytes(text)) != "Hi Mom -+Jjo--+ACE-" || utf7.GetByteCount(text) != 19) return 4;
        if (Ascii(new UTF7Encoding(true).GetBytes(text)) != "Hi Mom -+Jjo--!") return 5;
        if (Ascii(utf7.GetBytes("a+b")) != "a+-b" || utf7.GetString(Encoding.ASCII.GetBytes("a+-b")) != "a+b") return 6;
        if (utf7.GetString(Encoding.ASCII.GetBytes("Hi Mom -+Jjo--!")) != text || utf7.GetCharCount(Encoding.ASCII.GetBytes("+AGE")) != 1) return 7;
        if (utf7.GetString(new byte[] { 0x41, 0x80, 0x42 }) != "A\u0080B" || utf7.GetString(new byte[] { 0x2B, 0x41, 0x80 }) != "\u0080") return 8;
        if (utf7.GetMaxByteCount(10) != 32 || utf7.GetMaxCharCount(0) != 1 || utf7.GetMaxCharCount(5) != 5) return 9;
        if (!Throws<ArgumentException>(() => utf7.GetBytes("☺".ToCharArray(), 0, 1, new byte[2], 0))) return 10;
        if (!Throws<NotSupportedException>(() => Encoding.GetEncoding(65000)) || !Throws<NotSupportedException>(() => Encoding.GetEncoding("utf-7"))) return 11;
        var strict = (Encoding)utf7.Clone();
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        if (!Throws<DecoderFallbackException>(() => strict.GetString(new byte[] { 0x80 }))) return 12;

        // Decoders keep shifted bits between calls; encoders keep pending bits until flushed.
        Decoder decoder = utf7.GetDecoder();
        char[] chars = new char[4];
        if (decoder.GetChars(Encoding.ASCII.GetBytes("+AG"), 0, 3, chars, 0, false) != 0) return 13;
        if (decoder.GetChars(Encoding.ASCII.GetBytes("E-z"), 0, 3, chars, 0, true) != 2 || chars[0] != 'a' || chars[1] != 'z') return 14;
        Encoder encoder = utf7.GetEncoder();
        byte[] bytes = new byte[8];
        int first = encoder.GetBytes("☺".ToCharArray(), 0, 1, bytes, 0, false);
        int second = encoder.GetBytes(new[] { 'x' }, 0, 1, bytes, first, true);
        if (first != 3 || second != 3 || Ascii(bytes.AsSpan(0, 6).ToArray()) != "+Jjo-x") return 15;
        encoder.Reset();
        if (encoder.GetByteCount("☺".ToCharArray(), 0, 1, false) != 3 || encoder.GetByteCount("☺".ToCharArray(), 0, 1, true) != 5) return 16;
        encoder.Convert("a☺".ToCharArray(), 0, 2, bytes, 0, 3, true, out int charsUsed, out int bytesUsed, out bool completed);
        if (charsUsed != 2 || bytesUsed != 3 || completed || Ascii(bytes.AsSpan(0, 3).ToArray()) != "a+J") return 17;
        encoder.Convert(Array.Empty<char>(), 0, 0, bytes, 3, 5, true, out charsUsed, out bytesUsed, out completed);
        if (charsUsed != 0 || bytesUsed != 3 || !completed || Ascii(bytes.AsSpan(0, 6).ToArray()) != "a+Jjo-") return 18;

        // ICommand is an ordinary interface: callers dispatch and subscribe explicitly.
        var command = new Command();
        ICommand contract = command;
        int raised = 0;
        EventHandler handler = (_, _) => raised++;
        contract.CanExecuteChanged += handler;
        command.Raise();
        contract.CanExecuteChanged -= handler;
        command.Raise();
        if (raised != 1 || !contract.CanExecute("go") || contract.CanExecute(null)) return 19;
        contract.Execute("go");
        contract.Execute(null);
        if (command.Executed != 102) return 20;

        // Authentication exceptions keep their messages, causes and hierarchy.
        var cause = new InvalidOperationException("inner");
        var invalid = new InvalidCredentialException("bad", cause);
        if (invalid is not AuthenticationException || invalid.Message != "bad" || !ReferenceEquals(invalid.InnerException, cause)) return 21;
        if (new AuthenticationException("auth") is not SystemException || new InvalidCredentialException().InnerException is not null) return 22;

        // Extended protection policies validate their constructors and describe themselves.
        if ((int)PolicyEnforcement.Always != 2 || (int)ProtectionScenario.TrustedProxy != 1) return 23;
        if (!Throws<ArgumentException>(() => new ExtendedProtectionPolicy(PolicyEnforcement.Never, ProtectionScenario.TransportSelected, (ServiceNameCollection?)null))) return 24;
        if (!Throws<ArgumentException>(() => new ExtendedProtectionPolicy(PolicyEnforcement.Always, ProtectionScenario.TrustedProxy, new ArrayList()))) return 25;
        if (!Throws<ArgumentNullException>(() => new ExtendedProtectionPolicy(PolicyEnforcement.Always, (ChannelBinding)null!))) return 26;
        var never = new ExtendedProtectionPolicy(PolicyEnforcement.Never);
        if (never.ProtectionScenario != ProtectionScenario.TransportSelected || never.CustomServiceNames is not null || never.CustomChannelBinding is not null) return 27;
        var names = new ServiceNameCollection(new[] { "HTTP/Example.COM", "HTTP/example.com", "HTTP/other.example:8080/app" });
        var policy = new ExtendedProtectionPolicy(PolicyEnforcement.Always, ProtectionScenario.TrustedProxy, names);
        if (!ReferenceEquals(policy.CustomServiceNames, names) || names.Count != 2 || !names.Contains("http/EXAMPLE.com")) return 28;
        if (policy.ToString() != "ProtectionScenario=TrustedProxy; PolicyEnforcement=Always; CustomChannelBinding=<null>; ServiceNames=HTTP/Example.COM, HTTP/other.example:8080/app") return 29;
        var merged = names.Merge("SMTP/mail.example");
        if (merged.Count != 3 || names.Count != 2 || !merged.Contains("smtp/MAIL.example")) return 30;
        if (!Throws<InvalidCastException>(() => new ServiceNameCollection(new object[] { 1 })) || !Throws<ArgumentException>(() => names.Merge(new object[] { 1 }))) return 31;
        var converter = new ExtendedProtectionPolicyTypeConverter();
        if ((string?)converter.ConvertTo(null, CultureInfo.InvariantCulture, never, typeof(string)) != never.ToString()) return 32;
#if DESKTOP_ORACLE
        // OSSupportsExtendedProtection depends on the desktop host's authentication libraries.
        if (!converter.CanConvertTo(null, typeof(InstanceDescriptor))) return 33;
#else
        // NetWasm has no OS authentication, and InstanceDescriptor conversion needs reflection.
        if (ExtendedProtectionPolicy.OSSupportsExtendedProtection || converter.CanConvertTo(null, typeof(InstanceDescriptor))) return 33;
        if (!Throws<PlatformNotSupportedException>(() => converter.ConvertTo(null, null, never, typeof(InstanceDescriptor)))) return 34;
#endif

        // SecureStringMarshal forwards to Marshal's allocating copies.
        using (var secure = new SecureString())
        {
            foreach (char c in "päss") secure.AppendChar(c);
            IntPtr unicode = SecureStringMarshal.SecureStringToGlobalAllocUnicode(secure);
            try { if (Marshal.PtrToStringUni(unicode) != "päss") return 35; }
            finally { Marshal.ZeroFreeGlobalAllocUnicode(unicode); }
            IntPtr coTask = SecureStringMarshal.SecureStringToCoTaskMemUnicode(secure);
            try { if (Marshal.PtrToStringUni(coTask) != "päss") return 36; }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(coTask); }
        }
        using (var ascii = new SecureString())
        {
            foreach (char c in "key") ascii.AppendChar(c);
            IntPtr ansi = SecureStringMarshal.SecureStringToCoTaskMemAnsi(ascii);
            try { if (Marshal.PtrToStringAnsi(ansi) != "key") return 37; }
            finally { Marshal.ZeroFreeCoTaskMemAnsi(ansi); }
            IntPtr global = SecureStringMarshal.SecureStringToGlobalAllocAnsi(ascii);
            try { if (Marshal.PtrToStringAnsi(global) != "key") return 38; }
            finally { Marshal.ZeroFreeGlobalAllocAnsi(global); }
        }
        if (!Throws<ArgumentNullException>(() => SecureStringMarshal.SecureStringToGlobalAllocUnicode(null!))) return 39;

        // Designer serializer attributes keep their names and type identity.
        var serializer = new DesignerSerializerAttribute("Fixture.Serializer, Fixture", "Fixture.Base, Fixture");
        if (serializer.SerializerTypeName != "Fixture.Serializer, Fixture" || serializer.SerializerBaseTypeName != "Fixture.Base, Fixture") return 40;
        // TypeId is the attribute type's full name and the base type's name (issue 225: without type names a type's name reads its ID).
        if ((string)serializer.TypeId != typeof(DesignerSerializerAttribute).FullName + "Fixture.Base") return 41;
#if !DESKTOP_ORACLE
        // Type-taking constructors need assembly-qualified names, which NetWasm does not report.
        if (!Throws<PlatformNotSupportedException>(() => new DesignerSerializerAttribute(typeof(Command), typeof(EchoProxy)))) return 42;
#endif

        // DispatchProxy subclasses construct and dispatch; generating a proxy needs emitted code.
        var proxy = new EchoProxy();
        if ((string?)proxy.Call("x") != "hi x") return 43;
#if DESKTOP_ORACLE
        if (DispatchProxy.Create<IGreeter, EchoProxy>().Greet("y") != "hi y") return 44;
#else
        if (!Throws<PlatformNotSupportedException>(() => DispatchProxy.Create<IGreeter, EchoProxy>())) return 44;
#endif

        // Runtime reflection extensions validate their receivers before any lookup.
        if (!Throws<ArgumentNullException>(() => RuntimeReflectionExtensions.GetRuntimeFields(null!)) ||
            !Throws<ArgumentNullException>(() => RuntimeReflectionExtensions.GetRuntimeBaseDefinition(null!)) ||
            !Throws<ArgumentNullException>(() => RuntimeReflectionExtensions.GetMethodInfo(null!))) return 45;
#if !DESKTOP_ORACLE
        Func<int, int> twice = value => value * 2;
        if (!Throws<PlatformNotSupportedException>(() => typeof(Command).GetRuntimeMethods()) ||
            !Throws<PlatformNotSupportedException>(() => typeof(Command).GetRuntimeProperty("Executed")) ||
            !Throws<PlatformNotSupportedException>(() => twice.GetMethodInfo())) return 46;
#endif

        // IUnknownConstantAttribute supplies a fresh null wrapper.
        var unknown = new IUnknownConstantAttribute();
        if (unknown.Value is not UnknownWrapper wrapper || wrapper.WrappedObject is not null || ReferenceEquals(unknown.Value, unknown.Value)) return 47;

        // Span casing rejects overlap and reports a short destination without writing.
        Span<char> destination = stackalloc char[5];
        if ("HeLLo".AsSpan().ToLower(destination, CultureInfo.InvariantCulture) != 5 || destination.ToString() != "hello") return 48;
        if ("HeLLo".AsSpan().ToUpper(destination, null) != 5 || destination.ToString() != "HELLO") return 49;
        Span<char> small = stackalloc char[2];
        small[0] = 'z';
        if ("abc".AsSpan().ToUpper(small, CultureInfo.InvariantCulture) != -1 || "abc".AsSpan().ToLowerInvariant(small) != -1 || small[0] != 'z') return 50;
        char[] shared = "abc".ToCharArray();
        if (!Throws<InvalidOperationException>(() => ((ReadOnlySpan<char>)shared).ToUpperInvariant(shared)) ||
            !Throws<InvalidOperationException>(() => ((ReadOnlySpan<char>)shared).ToLower(shared, null))) return 51;

        // Uri's protected GetObjectData is formatter serialization.
        var uri = new SerializingUri("http://example.com/a");
#if DESKTOP_ORACLE
        var info = new SerializationInfo(typeof(Uri), new FormatterConverter());
        uri.Fill(info);
        if (info.GetString("AbsoluteUri") != "http://example.com/a") return 52;
#else
        if (!Throws<PlatformNotSupportedException>(() => uri.Fill(null!))) return 52;
#endif
        return input + 1;
    }
}
