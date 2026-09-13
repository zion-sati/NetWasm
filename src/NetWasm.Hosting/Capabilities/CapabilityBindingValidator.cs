using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Capabilities;

/// <summary>Proves that selected providers exactly satisfy and are authorized for a final deployment.</summary>
public sealed class CapabilityBindingValidator : ICapabilityBindingValidator
{
    private readonly IDeploymentManifestValidator _manifestValidator;
    private readonly INetWasmExecutionRequestValidator _requestValidator;
    private readonly IInternalImportPolicy _internalImports;

    public CapabilityBindingValidator(
        IDeploymentManifestValidator manifestValidator,
        INetWasmExecutionRequestValidator requestValidator)
        : this(manifestValidator, requestValidator, new InternalImportPolicy())
    {
    }

    internal CapabilityBindingValidator(
        IDeploymentManifestValidator manifestValidator,
        INetWasmExecutionRequestValidator requestValidator,
        IInternalImportPolicy internalImports)
    {
        _manifestValidator = manifestValidator ?? throw new ArgumentNullException(nameof(manifestValidator));
        _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
        _internalImports = internalImports ?? throw new ArgumentNullException(nameof(internalImports));
    }

    public void Validate(
        DeploymentManifest manifest,
        NetWasmExecutionRequest request,
        ImmutableArray<NetWasmPlatformImportProvider> platformProviders,
        ImmutableArray<NetWasmApplicationImportProvider> applicationProviders)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(request);
        _manifestValidator.Validate(manifest);
        _requestValidator.Validate(request);
        if (!string.Equals(manifest.BuildFingerprint, request.BuildFingerprint, StringComparison.Ordinal))
        {
            throw new ArgumentException("The execution request does not target this deployment build.", nameof(request));
        }

        if (platformProviders.IsDefault || applicationProviders.IsDefault)
        {
            throw new ArgumentException("Selected import provider collections must be explicit.", nameof(platformProviders));
        }

        var requiredModules = manifest.RequiredImportModules
            .Where(module => !_internalImports.IsInternal(module))
            .ToHashSet(StringComparer.Ordinal);
        var providerModules = new HashSet<string>(StringComparer.Ordinal);
        var platforms = ValidatePlatformProviders(platformProviders, requiredModules, providerModules, request.Grants);
        var applications = ValidateApplicationProviders(applicationProviders, requiredModules, providerModules);
        ValidateApplicationBindings(manifest, request, requiredModules, applications);
        ValidateRequiredImports(manifest.RequiredImports, platforms, applications);
        if (!providerModules.SetEquals(requiredModules))
        {
            throw new ArgumentException(
                "Every required import module must have exactly one selected provider.",
                nameof(platformProviders));
        }
    }

    private static Dictionary<string, NetWasmPlatformImportProvider> ValidatePlatformProviders(
        ImmutableArray<NetWasmPlatformImportProvider> providers,
        HashSet<string> requiredModules,
        HashSet<string> providerModules,
        NetWasmCapabilityGrants grants)
    {
        var validated = new Dictionary<string, NetWasmPlatformImportProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ValidateVersionedIdentifier(provider.Module);
            if (!IsReservedModule(provider.Module))
            {
                throw new ArgumentException("Platform providers must use reserved module keys.", nameof(providers));
            }

            ValidateProviderFunctions(provider.Module, provider.Functions);
            if (!requiredModules.Contains(provider.Module))
            {
                throw new ArgumentException("A selected platform provider is not required by the deployment.", nameof(providers));
            }

            if (!providerModules.Add(provider.Module))
            {
                throw new ArgumentException("Selected provider modules must be unique.", nameof(providers));
            }

            if (!IsGranted(provider.Capability, grants))
            {
                throw new ArgumentException("A required platform capability was not granted.", nameof(providers));
            }

            validated.Add(provider.Module, provider);
        }

        return validated;
    }

    private static Dictionary<string, NetWasmApplicationImportProvider> ValidateApplicationProviders(
        ImmutableArray<NetWasmApplicationImportProvider> providers,
        HashSet<string> requiredModules,
        HashSet<string> providerModules)
    {
        var validated = new Dictionary<string, NetWasmApplicationImportProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ValidateVersionedIdentifier(provider.Module);
            if (IsReservedModule(provider.Module))
            {
                throw new ArgumentException("Applications cannot provide reserved platform modules.", nameof(providers));
            }

            ValidateProviderFunctions(provider.Module, provider.Functions);
            if (!requiredModules.Contains(provider.Module))
            {
                throw new ArgumentException("A selected application provider is not required by the deployment.", nameof(providers));
            }

            if (!providerModules.Add(provider.Module))
            {
                throw new ArgumentException("Selected provider modules must be unique.", nameof(providers));
            }

            validated.Add(provider.Module, provider);
        }

        return validated;
    }

    private static void ValidateApplicationBindings(
        DeploymentManifest manifest,
        NetWasmExecutionRequest request,
        HashSet<string> requiredModules,
        Dictionary<string, NetWasmApplicationImportProvider> providers)
    {
        var artifacts = manifest.Artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
        var bindings = request.ApplicationImports.ToDictionary(binding => binding.Module, StringComparer.Ordinal);
        foreach (var binding in request.ApplicationImports)
        {
            if (!requiredModules.Contains(binding.Module))
            {
                throw new ArgumentException("An application import binding is not required by the deployment.", nameof(request));
            }

            if (!providers.ContainsKey(binding.Module))
            {
                throw new ArgumentException("An application import binding has no selected provider.", nameof(request));
            }

            if (!artifacts.TryGetValue(binding.ArtifactPath, out var artifact))
            {
                throw new ArgumentException("An application import artifact is missing from the deployment.", nameof(request));
            }

            if (!string.Equals(artifact.Role, "application-import", StringComparison.Ordinal)
                || !string.Equals(artifact.MediaType, "text/javascript", StringComparison.Ordinal)
                || !string.Equals(artifact.Sha256, binding.Sha256, StringComparison.Ordinal))
            {
                throw new ArgumentException("An application import does not match its deployment artifact contract.", nameof(request));
            }
        }

        foreach (var provider in providers.Values)
        {
            if (!bindings.ContainsKey(provider.Module))
            {
                throw new ArgumentException("A selected application provider has no request binding.", nameof(request));
            }
        }
    }

    private void ValidateRequiredImports(
        ImmutableArray<DeploymentFunction> requiredImports,
        Dictionary<string, NetWasmPlatformImportProvider> platformProviders,
        Dictionary<string, NetWasmApplicationImportProvider> applicationProviders)
    {
        foreach (var requiredImport in requiredImports)
        {
            if (_internalImports.IsInternal(requiredImport.Interface))
            {
                continue;
            }
            var functions = IsReservedModule(requiredImport.Interface)
                ? GetRequiredProvider(platformProviders, requiredImport.Interface).Functions
                : GetRequiredProvider(applicationProviders, requiredImport.Interface).Functions;
            var providedFunction = functions.SingleOrDefault(function =>
                string.Equals(function.Name, requiredImport.Name, StringComparison.Ordinal));
            if (providedFunction is null)
            {
                throw new ArgumentException("A selected provider is missing a required import member.", nameof(requiredImports));
            }

            if (!providedFunction.Parameters.SequenceEqual(requiredImport.Parameters, StringComparer.Ordinal)
                || !providedFunction.Results.SequenceEqual(requiredImport.Results, StringComparer.Ordinal))
            {
                throw new ArgumentException("A selected provider has an incompatible required import signature.", nameof(requiredImports));
            }
        }
    }

    private static TProvider GetRequiredProvider<TProvider>(
        Dictionary<string, TProvider> providers,
        string module)
        where TProvider : class
    {
        if (!providers.TryGetValue(module, out var provider))
        {
            throw new ArgumentException("A final required import has no selected provider.", nameof(providers));
        }

        return provider;
    }

    private static void ValidateProviderFunctions(string module, ImmutableArray<DeploymentFunction> functions)
    {
        if (functions.IsDefault)
        {
            throw new ArgumentException("Selected provider functions must be explicit.", nameof(functions));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            ArgumentNullException.ThrowIfNull(function);
            if (!string.Equals(function.Interface, module, StringComparison.Ordinal))
            {
                throw new ArgumentException("Provider functions must belong to their provider module.", nameof(functions));
            }

            ValidateFunctionName(function.Name);
            ValidateSignature(function.Parameters);
            ValidateSignature(function.Results);
            if (!names.Add(function.Name))
            {
                throw new ArgumentException("Provider function names must be unique within a module.", nameof(functions));
            }
        }
    }

    private static bool IsGranted(NetWasmPlatformCapability capability, NetWasmCapabilityGrants grants) => capability switch
    {
        NetWasmPlatformCapability.Baseline => true,
        NetWasmPlatformCapability.Environment => true,
        NetWasmPlatformCapability.PreopenedDirectories => true,
        NetWasmPlatformCapability.Network => grants.Network == NetWasmNetworkPolicy.AllowAll,
        NetWasmPlatformCapability.WallClock => grants.Clocks.Contains(NetWasmClock.Wall),
        NetWasmPlatformCapability.MonotonicClock => grants.Clocks.Contains(NetWasmClock.Monotonic),
        NetWasmPlatformCapability.Randomness => grants.Randomness,
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };

    private static bool IsReservedModule(string module) =>
        module.StartsWith("wasi:", StringComparison.Ordinal)
        || module.StartsWith("netwasm:platform/", StringComparison.Ordinal);

    private static void ValidateVersionedIdentifier(string value)
    {
        ValidateText(value);
        var separator = value.LastIndexOf('@');
        if (separator <= 0 || separator == value.Length - 1 || value.AsSpan(0, separator).Contains('@'))
        {
            throw new ArgumentException("Provider modules require one exact version.", nameof(value));
        }

        if (!(char.IsAsciiLetterLower(value[0]) || char.IsAsciiDigit(value[0])))
        {
            throw new ArgumentException("Provider modules must begin with a lowercase letter or digit.", nameof(value));
        }

        foreach (var character in value.AsSpan(0, separator))
        {
            if (!(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.' or ':' or '/'))
            {
                throw new ArgumentException("Provider modules must use canonical lowercase syntax.", nameof(value));
            }
        }

        var versionParts = value[(separator + 1)..].Split('.');
        if (versionParts.Length != 3 || versionParts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)))
        {
            throw new ArgumentException("Provider modules require an exact numeric version.", nameof(value));
        }
    }

    private static void ValidateFunctionName(string value)
    {
        if (!DeploymentFunctionName.IsCanonical(value))
        {
            throw new ArgumentException(
                "Provider function names must use canonical WIT identity syntax.",
                nameof(value));
        }
    }

    private static void ValidateSignature(ImmutableArray<string> values)
    {
        if (values.IsDefault)
        {
            throw new ArgumentException("Provider function signatures must be explicit.", nameof(values));
        }

        foreach (var value in values)
        {
            ValidateText(value);
        }
    }

    private static void ValidateText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.AsSpan().Trim().Length != value.Length || value.Any(char.IsControl))
        {
            throw new ArgumentException("Provider contract text must be canonical and contain no control characters.", nameof(value));
        }
    }
}
