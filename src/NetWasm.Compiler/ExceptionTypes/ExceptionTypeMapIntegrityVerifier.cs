using System;

namespace NetWasm.Compiler.ExceptionTypes;

public interface IExceptionTypeMapIntegrityVerifier
{
    void Verify(ExceptionTypeMapArtifact map);
}

public sealed class ExceptionTypeMapIntegrityVerifier(
    IArtifactDigestCalculator digests) : IExceptionTypeMapIntegrityVerifier
{
    private readonly IArtifactDigestCalculator _digests = digests ??
        throw new ArgumentNullException(nameof(digests));

    public void Verify(ExceptionTypeMapArtifact map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var digest = _digests.Calculate(map.Bytes);
        if (!string.Equals(digest, map.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Exception type map digest does not match its bytes.");
        }
    }
}
