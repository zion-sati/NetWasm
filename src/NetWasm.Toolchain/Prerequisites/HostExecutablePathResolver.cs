using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class HostExecutablePathResolver : IHostExecutablePathResolver
{
    private const string PathVariableName = "PATH";

    private readonly IHostEnvironmentVariableReader _environment;
    private readonly IFilePresenceChecker _presenceChecker;
    private readonly IHostPathCanonicalizer _pathCanonicalizer;
    private readonly HostExecutablePathConvention _convention;
    private readonly StringComparer _pathComparer;

    public HostExecutablePathResolver(
        IHostEnvironmentVariableReader environment,
        IFilePresenceChecker presenceChecker,
        IHostPathCanonicalizer pathCanonicalizer,
        HostExecutablePathConvention convention)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(presenceChecker);
        ArgumentNullException.ThrowIfNull(pathCanonicalizer);
        ArgumentNullException.ThrowIfNull(convention);

        if (convention.PathSeparator == '\0')
        {
            throw new ArgumentException("The PATH separator cannot be null.", nameof(convention));
        }

        if (convention.ExecutableSuffix is null ||
            (convention.ExecutableSuffix.Length > 0 &&
             (!convention.ExecutableSuffix.StartsWith('.') ||
              convention.ExecutableSuffix.Contains('/') ||
              convention.ExecutableSuffix.Contains('\\'))))
        {
            throw new ArgumentException(
                "The executable suffix must be empty or a file-name suffix beginning with '.'.",
                nameof(convention));
        }

        _pathComparer = convention.PathComparison switch
        {
            StringComparison.Ordinal => StringComparer.Ordinal,
            StringComparison.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
            _ => throw new ArgumentException(
                "Host executable paths require ordinal comparison semantics.",
                nameof(convention)),
        };
        _environment = environment;
        _presenceChecker = presenceChecker;
        _pathCanonicalizer = pathCanonicalizer;
        _convention = convention;
    }

    public ResolvedHostExecutable Resolve(HostExecutableResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToolId);
        RequireExecutableName(request.ExecutableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OverrideEnvironmentVariableName);
        RequireEnvironmentFallbacks(request.EnvironmentFallbacks);
        RequireFallbacks(request.RootFallbacks);

        var overridePath = _environment.Read(request.OverrideEnvironmentVariableName);
        if (overridePath is not null)
        {
            return ResolveOverride(request, overridePath);
        }

        var environmentFallback = ResolveEnvironmentFallback(request);
        if (environmentFallback is not null)
        {
            return environmentFallback;
        }

        var executableFileName = AddExecutableSuffix(request.ExecutableName);
        var path = _environment.Read(PathVariableName);
        if (!string.IsNullOrWhiteSpace(path))
        {
            var candidates = BuildCandidates(request.ToolId, path, executableFileName);
            foreach (var candidate in candidates)
            {
                if (_presenceChecker.Exists(candidate))
                {
                    return new(request.ToolId, candidate, HostExecutableResolutionSource.Path);
                }
            }
        }

        var fallback = ResolveFallback(request, executableFileName);
        if (fallback is not null)
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new HostExecutableResolutionException(
                request.ToolId,
                HostExecutableResolutionFailure.PathUnavailable,
                $"Cannot locate '{request.ExecutableName}' because PATH is unavailable{DescribeFallbacks(request)}.");
        }

        throw new HostExecutableResolutionException(
            request.ToolId,
            HostExecutableResolutionFailure.ExecutableNotFound,
            $"Cannot find '{executableFileName}' for host tool '{request.ToolId}' on PATH{DescribeFallbacks(request)}.");
    }

    private ResolvedHostExecutable? ResolveEnvironmentFallback(
        HostExecutableResolutionRequest request)
    {
        foreach (var fallback in request.EnvironmentFallbacks)
        {
            var configuredPath = _environment.Read(fallback.EnvironmentVariableName);
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                continue;
            }
            if (!Path.IsPathFullyQualified(configuredPath))
            {
                throw new HostExecutableResolutionException(
                    request.ToolId,
                    HostExecutableResolutionFailure.InvalidFallback,
                    $"{fallback.EnvironmentVariableName} must contain an absolute executable path.");
            }

            var candidate = Canonicalize(
                request.ToolId,
                HostExecutableResolutionFailure.InvalidFallback,
                configuredPath,
                $"{fallback.EnvironmentVariableName} does not contain a valid executable path.");
            if (!_presenceChecker.Exists(candidate))
            {
                throw new HostExecutableResolutionException(
                    request.ToolId,
                    HostExecutableResolutionFailure.ExecutableNotFound,
                    $"The executable selected by {fallback.EnvironmentVariableName} does not exist.");
            }
            return new(
                request.ToolId,
                candidate,
                HostExecutableResolutionSource.EnvironmentExecutable);
        }

        return null;
    }

    private ResolvedHostExecutable? ResolveFallback(
        HostExecutableResolutionRequest request,
        string executableFileName)
    {
        foreach (var fallback in request.RootFallbacks)
        {
            var configuredRoot = _environment.Read(fallback.RootEnvironmentVariableName);
            if (string.IsNullOrWhiteSpace(configuredRoot))
            {
                continue;
            }
            if (!Path.IsPathFullyQualified(configuredRoot))
            {
                throw new HostExecutableResolutionException(
                    request.ToolId,
                    HostExecutableResolutionFailure.InvalidFallback,
                    $"{fallback.RootEnvironmentVariableName} must contain an absolute installation root.");
            }

            var root = Canonicalize(
                request.ToolId,
                HostExecutableResolutionFailure.InvalidFallback,
                configuredRoot,
                $"{fallback.RootEnvironmentVariableName} does not contain a valid installation root.");
            var candidate = Canonicalize(
                request.ToolId,
                HostExecutableResolutionFailure.InvalidFallback,
                Path.Combine(root, fallback.RelativeDirectory, executableFileName),
                $"{fallback.RootEnvironmentVariableName} does not produce a valid host-tool path.");
            RequireContainedFallback(request.ToolId, root, candidate);
            if (_presenceChecker.Exists(candidate))
            {
                return new(
                    request.ToolId,
                    candidate,
                    HostExecutableResolutionSource.EnvironmentRoot);
            }
        }

        return null;
    }

    private ResolvedHostExecutable ResolveOverride(
        HostExecutableResolutionRequest request,
        string overridePath)
    {
        if (string.IsNullOrWhiteSpace(overridePath) || !Path.IsPathFullyQualified(overridePath))
        {
            throw new HostExecutableResolutionException(
                request.ToolId,
                HostExecutableResolutionFailure.InvalidOverride,
                $"{request.OverrideEnvironmentVariableName} must contain an absolute executable path.");
        }

        var absolutePath = Canonicalize(
            request.ToolId,
            HostExecutableResolutionFailure.InvalidOverride,
            overridePath,
            $"{request.OverrideEnvironmentVariableName} does not contain a valid executable path.");
        if (!_presenceChecker.Exists(absolutePath))
        {
            throw new HostExecutableResolutionException(
                request.ToolId,
                HostExecutableResolutionFailure.ExecutableNotFound,
                $"The executable selected by {request.OverrideEnvironmentVariableName} does not exist.");
        }

        return new(request.ToolId, absolutePath, HostExecutableResolutionSource.Override);
    }

    private ImmutableArray<string> BuildCandidates(
        string toolId,
        string path,
        string executableFileName)
    {
        var candidates = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(_pathComparer);
        foreach (var directory in path.Split(_convention.PathSeparator, StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            var candidate = Canonicalize(
                toolId,
                HostExecutableResolutionFailure.InvalidPathEntry,
                Path.Combine(directory, executableFileName),
                "PATH contains an invalid host-tool candidate.");
            if (seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates.ToImmutable();
    }

    private string AddExecutableSuffix(string executableName)
        => _convention.ExecutableSuffix.Length == 0 ||
           executableName.EndsWith(_convention.ExecutableSuffix, _convention.PathComparison)
            ? executableName
            : executableName + _convention.ExecutableSuffix;

    private string Canonicalize(
        string toolId,
        HostExecutableResolutionFailure failure,
        string path,
        string message)
    {
        try
        {
            return _pathCanonicalizer.Canonicalize(path);
        }
        catch (ArgumentException exception)
        {
            throw new HostExecutableResolutionException(toolId, failure, message, exception);
        }
    }

    private static void RequireExecutableName(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        if (executableName is "." or ".." ||
            executableName.Contains('/') ||
            executableName.Contains('\\'))
        {
            throw new ArgumentException(
                "The executable name must be a single file name.",
                nameof(executableName));
        }
    }

    private static void RequireFallbacks(
        ImmutableArray<HostExecutableRootFallback> fallbacks)
    {
        if (fallbacks.IsDefault)
        {
            throw new ArgumentException(
                "Host executable root fallbacks must be explicit.",
                nameof(fallbacks));
        }
        foreach (var fallback in fallbacks)
        {
            ArgumentNullException.ThrowIfNull(fallback);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                fallback.RootEnvironmentVariableName);
            ArgumentException.ThrowIfNullOrWhiteSpace(fallback.RelativeDirectory);
            if (Path.IsPathFullyQualified(fallback.RelativeDirectory)
                || fallback.RelativeDirectory.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
            {
                throw new ArgumentException(
                    "Host executable fallback directories must be contained relative paths.",
                    nameof(fallbacks));
            }
        }
    }

    private static void RequireEnvironmentFallbacks(
        ImmutableArray<HostExecutableEnvironmentFallback> fallbacks)
    {
        if (fallbacks.IsDefault)
        {
            throw new ArgumentException(
                "Host executable environment fallbacks must be explicit.",
                nameof(fallbacks));
        }
        foreach (var fallback in fallbacks)
        {
            ArgumentNullException.ThrowIfNull(fallback);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                fallback.EnvironmentVariableName);
        }
    }

    private static void RequireContainedFallback(
        string toolId,
        string root,
        string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        if (string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            throw new HostExecutableResolutionException(
                toolId,
                HostExecutableResolutionFailure.InvalidFallback,
                "A host executable fallback escaped its configured installation root.");
        }
    }

    private static string DescribeFallbacks(HostExecutableResolutionRequest request) =>
        request.EnvironmentFallbacks.Length == 0 && request.RootFallbacks.Length == 0
            ? string.Empty
            : $" or through the configured {string.Join(
                ", ",
                request.EnvironmentFallbacks.Select(static fallback =>
                        $"{fallback.EnvironmentVariableName} executable")
                    .Concat(request.RootFallbacks.Select(static fallback =>
                        $"{fallback.RootEnvironmentVariableName} installation root")))}";
}
