using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Hosting.Execution;

/// <summary>Validates ephemeral values, authority and application bindings before host execution.</summary>
public sealed class NetWasmExecutionRequestValidator : INetWasmExecutionRequestValidator
{
    public void Validate(NetWasmExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported NetWasm execution request schema.", nameof(request));
        }

        ValidateDigest(request.BuildFingerprint);
        ValidateDigest(request.DeploymentManifestSha256);
        ValidateArguments(request.Arguments);
        ValidateGrants(request.Grants);
        ValidateEnvironment(request.Environment, request.Grants.Environment);
        ValidateApplicationImports(request.ApplicationImports);
    }

    private static void ValidateArguments(ImmutableArray<string> arguments)
    {
        if (arguments.IsDefault)
        {
            throw new ArgumentException("Execution arguments must be explicit.", nameof(arguments));
        }

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (argument.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("Execution arguments must not contain NUL.", nameof(arguments));
            }
        }
    }

    private static void ValidateGrants(NetWasmCapabilityGrants grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (!Enum.IsDefined(grants.Network))
        {
            throw new ArgumentException("Unsupported NetWasm network policy.", nameof(grants));
        }

        if (grants.Environment.IsDefault || grants.Preopens.IsDefault || grants.Clocks.IsDefault)
        {
            throw new ArgumentException("Capability grant collections must be explicit.", nameof(grants));
        }

        var environment = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in grants.Environment)
        {
            ValidateEnvironmentName(name);
            if (!environment.Add(name))
            {
                throw new ArgumentException("Granted environment names must be unique.", nameof(grants));
            }
        }

        var guestPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preopen in grants.Preopens)
        {
            ArgumentNullException.ThrowIfNull(preopen);
            ValidateHostPath(preopen.HostPath);
            ValidateGuestPath(preopen.GuestPath);
            if (!Enum.IsDefined(preopen.Access))
            {
                throw new ArgumentException("Unsupported NetWasm preopen access.", nameof(grants));
            }

            if (!guestPaths.Add(preopen.GuestPath))
            {
                throw new ArgumentException("Guest preopen paths must be unique.", nameof(grants));
            }
        }

        var clocks = new HashSet<NetWasmClock>();
        foreach (var clock in grants.Clocks)
        {
            if (!Enum.IsDefined(clock))
            {
                throw new ArgumentException("Unsupported NetWasm clock grant.", nameof(grants));
            }

            if (!clocks.Add(clock))
            {
                throw new ArgumentException("Clock grants must be unique.", nameof(grants));
            }
        }
    }

    private static void ValidateEnvironment(
        ImmutableArray<NetWasmEnvironmentVariable> variables,
        ImmutableArray<string> grantedNames)
    {
        if (variables.IsDefault)
        {
            throw new ArgumentException("Execution environment must be explicit.", nameof(variables));
        }

        var grants = grantedNames.ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            ArgumentNullException.ThrowIfNull(variable);
            ValidateEnvironmentName(variable.Name);
            ArgumentNullException.ThrowIfNull(variable.Value);
            if (variable.Value.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("Environment values must not contain NUL.", nameof(variables));
            }

            if (!names.Add(variable.Name))
            {
                throw new ArgumentException("Environment names must be unique.", nameof(variables));
            }

            if (!grants.Contains(variable.Name))
            {
                throw new ArgumentException("An environment value was supplied without an explicit grant.", nameof(variables));
            }
        }
    }

    private static void ValidateApplicationImports(ImmutableArray<NetWasmApplicationImport> imports)
    {
        if (imports.IsDefault)
        {
            throw new ArgumentException("Application import bindings must be explicit.", nameof(imports));
        }

        var modules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var import in imports)
        {
            ArgumentNullException.ThrowIfNull(import);
            ValidateVersionedIdentifier(import.Module);
            if (import.Module.StartsWith("wasi:", StringComparison.Ordinal)
                || import.Module.StartsWith("netwasm:platform/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Reserved platform modules cannot be provided by an application.", nameof(imports));
            }

            if (!modules.Add(import.Module))
            {
                throw new ArgumentException("Application import module keys must be unique.", nameof(imports));
            }

            ValidateArtifactPath(import.ArtifactPath);
            ValidateDigest(import.Sha256);
        }
    }

    private static void ValidateEnvironmentName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('=', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Environment names must not contain equals or NUL.", nameof(name));
        }
    }

    private static void ValidateHostPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Preopen host paths must be fully qualified local paths without NUL.", nameof(path));
        }
    }

    private static void ValidateGuestPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path[0] != '/' || path.Contains('\\', StringComparison.Ordinal) || path.Contains('\0', StringComparison.Ordinal)
            || path.AsSpan().Trim().Length != path.Length)
        {
            throw new ArgumentException("Guest preopen paths must use canonical absolute slash syntax.", nameof(path));
        }

        if (path.Length > 1 && path[1..].Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("Guest preopen paths must not contain empty or traversal segments.", nameof(path));
        }
    }

    private static void ValidateArtifactPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path[0] == '/' || path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal)
            || path.Contains('\0', StringComparison.Ordinal) || path.AsSpan().Trim().Length != path.Length)
        {
            throw new ArgumentException("Application import artifacts require canonical relative slash paths.", nameof(path));
        }

        if (path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("Application import artifact paths must not contain empty or traversal segments.", nameof(path));
        }
    }

    private static void ValidateVersionedIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1 || value.AsSpan(0, separator).Contains('@')
            || value.AsSpan().Trim().Length != value.Length)
        {
            throw new ArgumentException("Application module keys require one exact version.", nameof(value));
        }

        foreach (var character in value.AsSpan(0, separator))
        {
            if (!(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.' or ':' or '/'))
            {
                throw new ArgumentException("Application module keys must use canonical lowercase syntax.", nameof(value));
            }
        }

        if (!(char.IsAsciiLetterLower(value[0]) || char.IsAsciiDigit(value[0])))
        {
            throw new ArgumentException("Application module keys must begin with a lowercase letter or digit.", nameof(value));
        }

        var versionParts = value[(separator + 1)..].Split('.');
        if (versionParts.Length != 3 || versionParts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)))
        {
            throw new ArgumentException("Application module keys require an exact numeric version.", nameof(value));
        }
    }

    private static void ValidateDigest(string digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("Execution identities require lowercase SHA-256 digests.", nameof(digest));
        }
    }
}
