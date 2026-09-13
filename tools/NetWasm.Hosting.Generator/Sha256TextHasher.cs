using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Hosting.Generator;

internal interface ITextHasher
{
    string Hash(string value);
}

internal sealed class Sha256TextHasher : ITextHasher
{
    public string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
