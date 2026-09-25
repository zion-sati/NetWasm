using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusSourceFingerprint
{
    string Compute(CorpusFixture fixture);
}

internal sealed class CorpusSourceFingerprint : ICorpusSourceFingerprint
{
    public string Compute(CorpusFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(fixture.Source);
        if (fixture.AdditionalSources.IsDefault)
        {
            throw new ArgumentException("Additional sources must be an initialized collection.", nameof(fixture));
        }
        var bytes = fixture.AdditionalSources.IsEmpty
            ? Encoding.UTF8.GetBytes(fixture.Source)
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1,
                PrimarySource = fixture.Source,
                fixture.AdditionalSources,
            });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
