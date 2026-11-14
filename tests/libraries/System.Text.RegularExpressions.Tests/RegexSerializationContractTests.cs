using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Text.RegularExpressions.Tests;

#pragma warning disable SYSLIB0051 // The formatter constructors under test are obsolete.

// The netstandard ISerializable surface of Regex and RegexMatchTimeoutException: it binds, and formatter construction
// is unsupported.
public sealed class RegexSerializationContractTests
{
    [Test]
    public async Task FormatterConstructionBindsAndThrowsPlatformNotSupported()
    {
        await Assert.That(new Regex("a") is ISerializable).IsTrue();
        await Assert.That(new RegexMatchTimeoutException() is ISerializable).IsTrue();

        // Regex rejects formatter construction on every platform.
        await Assert.That(ThrowsPlatformNotSupported(() => _ = new SerializedRegex())).IsTrue();
#if NETWASM
        // Exception formatter deserialization is unavailable on NetWasm, so the timeout exception's constructor throws.
        await Assert.That(ThrowsPlatformNotSupported(() => _ = new SerializedTimeoutException())).IsTrue();
#endif
    }

    private static bool ThrowsPlatformNotSupported(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return true;
        }
    }

    private sealed class SerializedRegex : Regex
    {
        public SerializedRegex()
            : base(null!, default(StreamingContext))
        {
        }
    }

    private sealed class SerializedTimeoutException : RegexMatchTimeoutException
    {
        public SerializedTimeoutException()
            : base(null!, default(StreamingContext))
        {
        }
    }
}
