using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IAssemblyIdentityFormatter
{
    string Format(AssemblyIdentity identity);
}

public interface IAssemblyIdentityFormatterFactory
{
    IAssemblyIdentityFormatter Create(ImmutableArray<ManagedAssembly> assemblies);
}

public sealed class AssemblyIdentityFormatterFactory : IAssemblyIdentityFormatterFactory
{
    public IAssemblyIdentityFormatter Create(ImmutableArray<ManagedAssembly> assemblies) =>
        new AssemblyIdentityFormatter(assemblies);
}

public sealed class AssemblyIdentityFormatter(
    ImmutableArray<ManagedAssembly> assemblies) : IAssemblyIdentityFormatter
{
    public string Format(AssemblyIdentity identity)
    {
        var assembly = assemblies.Single(candidate => candidate.Identity == identity);
        var definition = assembly.Reader.GetAssemblyDefinition();
        var culture = definition.Culture.IsNil
            ? "neutral"
            : assembly.Reader.GetString(definition.Culture);
        var token = definition.PublicKey.IsNil
            ? "null"
            : CalculatePublicKeyToken(
                assembly.Reader.GetBlobBytes(definition.PublicKey));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{identity.Name}, Version={definition.Version}, Culture={culture}, " +
            $"PublicKeyToken={token}");
    }

    [SuppressMessage(
        "Security",
        "CA5350",
        Justification = "The ECMA-335 assembly public-key token is defined as the low " +
                        "64 bits of a SHA-1 digest.")]
    private static string CalculatePublicKeyToken(byte[] publicKey)
    {
        var hash = SHA1.HashData(publicKey);
        Array.Reverse(hash, hash.Length - 8, 8);
        return Convert.ToHexStringLower(hash.AsSpan(hash.Length - 8, 8));
    }
}
